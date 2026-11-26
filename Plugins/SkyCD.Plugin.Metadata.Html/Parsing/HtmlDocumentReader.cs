using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OfficeIMO.Html;
using SkyCD.Plugin.Metadata.Html.Exceptions;
using SkyCD.Plugin.Metadata.Html.Models;

namespace SkyCD.Plugin.Metadata.Html.Parsing;

/// <summary>
/// Parsed HTML facts shared by the metadata and preview capabilities: head fields, structure counts and
/// the document's visible text.
/// </summary>
/// <remarks>
/// The document is parsed once from an in-memory string with
/// <c>HtmlConversionDocumentOptions.UntrustedDefaults</c>, so no network or external file is fetched and
/// scripts are never executed: the semantic model keeps visible text while links become counts. The
/// parse-time character bound mirrors the plugin's source bound, so an oversized document fails with a
/// clear error instead of an unbounded parse.
/// </remarks>
internal sealed record HtmlDocumentData(
    string Title,
    string Language,
    string? Description,
    string? Keywords,
    string? Author,
    string? Generator,
    string? DeclaredCharset,
    bool HasBaseHref,
    int LinkCount,
    int ImageCount,
    int ScriptCount,
    string Text,
    IReadOnlyList<HtmlPreviewLine> Lines);

internal static class HtmlDocumentReader
{
    public static HtmlDocumentData Read(string html, HtmlPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var documentOptions = HtmlConversionDocumentOptions.UntrustedDefaults.Clone();
        var limits = documentOptions.Limits?.Clone() ?? HtmlConversionLimits.UntrustedDefaults.Clone();
        limits.MaxInputCharacters = (int)Math.Min(options.MaxSourceBytes, int.MaxValue);
        documentOptions.Limits = limits;

        HtmlConversionDocument conversion;
        try
        {
            conversion = HtmlConversionDocument.Parse(html, documentOptions, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new HtmlDecodeException("the document structure could not be parsed", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var semantics = conversion.SemanticDocument;
        var document = conversion.Document;

        var (text, lines) = CollectContent(semantics, options, cancellationToken);

        return new HtmlDocumentData(
            Title: semantics.Title?.Trim() ?? string.Empty,
            Language: semantics.Language?.Trim() ?? string.Empty,
            Description: FirstMetadata(semantics.Metadata, "description"),
            Keywords: FirstMetadata(semantics.Metadata, "keywords"),
            Author: FirstMetadata(semantics.Metadata, "author"),
            Generator: FirstMetadata(semantics.Metadata, "generator"),
            DeclaredCharset: ReadDeclaredCharset(document),
            HasBaseHref: document.QuerySelector("base[href]") is not null,
            LinkCount: document.QuerySelectorAll("a[href]").Count,
            ImageCount: document.QuerySelectorAll("img").Count,
            ScriptCount: document.QuerySelectorAll("script").Count,
            Text: text,
            Lines: lines);
    }

    private static string? FirstMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string name)
    {
        if (metadata.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return null;
    }

    private static string? ReadDeclaredCharset(OfficeIMO.Html.Dom.HtmlDocument document)
    {
        var direct = NormalizeCharset(document.QuerySelector("meta[charset]")?.GetAttribute("charset"));
        if (direct is not null)
        {
            return direct;
        }

        foreach (var meta in document.QuerySelectorAll("meta[http-equiv]"))
        {
            if (!string.Equals(meta.GetAttribute("http-equiv"), "content-type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var charset = ParseCharsetFromContentType(meta.GetAttribute("content"));
            if (charset is not null)
            {
                return charset;
            }
        }

        return null;
    }

    private static string? NormalizeCharset(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ParseCharsetFromContentType(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var index = content.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var token = content[(index + "charset=".Length)..].Trim();
        if (token.Length == 0)
        {
            return null;
        }

        var end = token.IndexOfAny([';', ' ', '\t', '\r', '\n', '"', '\'']);
        if (end >= 0)
        {
            token = token[..end];
        }

        return NormalizeCharset(token.Trim('"', '\''));
    }

    /// <summary>
    /// Walks sections in document order so the text reads as the page presents it: the full text drives
    /// the metadata counts and excerpt, while the preview lines stay at the configured bounds.
    /// </summary>
    /// <remarks>
    /// A heading that opens a section becomes the section title instead of a block, so the title joins
    /// the text as a heading line; generated and document titles are not body text and are skipped. A
    /// block that aggregates its children (a list keeps its items, with markers) is emitted once: a
    /// child whose text is already contained in the parent is not walked again, while a child with text
    /// of its own (a nested list under its item) still is.
    /// </remarks>
    private static (string Text, IReadOnlyList<HtmlPreviewLine> Lines) CollectContent(
        HtmlSemanticDocument semantics,
        HtmlPluginOptions options,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var lines = new List<HtmlPreviewLine>();
        var characters = 0;

        foreach (var section in semantics.Sections)
        {
            if (section.TitleSource == HtmlSemanticSectionTitleSource.Heading &&
                !string.IsNullOrWhiteSpace(section.Title))
            {
                Emit(section.Title.Trim(), isHeading: true);
            }

            foreach (var block in section.Blocks)
            {
                AppendBlock(block);
            }
        }

        return (text.ToString(), lines);

        void AppendBlock(HtmlSemanticBlock block)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var condensed = Condense(block.Text ?? string.Empty);
            if (condensed.Length > 0)
            {
                Emit(condensed, block.Kind == HtmlSemanticBlockKind.Heading);

                foreach (var child in block.Children)
                {
                    var childText = Condense(child.Text ?? string.Empty);
                    if (childText.Length > 0 &&
                        condensed.Contains(childText, StringComparison.Ordinal))
                    {
                        // Already emitted as part of the parent; walking it would duplicate the text.
                        continue;
                    }

                    AppendBlock(child);
                }
            }
            else
            {
                foreach (var child in block.Children)
                {
                    AppendBlock(child);
                }
            }
        }

        void Emit(string condensed, bool isHeading)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(condensed);

            if (lines.Count < options.MaxRenderedBlocks && characters < options.MaxRenderedCharacters)
            {
                var remaining = options.MaxRenderedCharacters - characters;
                var line = condensed.Length > remaining ? Truncate(condensed, remaining) : condensed;

                if (line.Length > 0)
                {
                    lines.Add(new HtmlPreviewLine(line, isHeading));
                    characters += line.Length;
                }
            }
        }
    }

    /// <summary>Normalizes line endings, drops blank lines and collapses internal whitespace runs.</summary>
    private static string Condense(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(text.Length);
        var pendingLineBreak = false;
        var pendingSpace = false;

        foreach (var rawLine in lines)
        {
            var hasContent = false;
            foreach (var ch in rawLine)
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                else if (pendingLineBreak)
                {
                    builder.Append('\n');
                }

                pendingLineBreak = false;
                builder.Append(ch);
                hasContent = true;
            }

            if (hasContent)
            {
                pendingLineBreak = true;
                pendingSpace = false;
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if (length >= text.Length)
        {
            return text;
        }

        if (char.IsHighSurrogate(text[length - 1]))
        {
            // Do not split a surrogate pair; a lone surrogate is not a valid stored string.
            length--;
        }

        return text[..length];
    }
}
