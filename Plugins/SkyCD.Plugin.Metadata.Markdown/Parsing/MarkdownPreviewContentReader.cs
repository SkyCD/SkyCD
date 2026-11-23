using System;
using System.Collections.Generic;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigMarkdown = Markdig.Markdown;
using SkyCD.Plugin.Metadata.Markdown.Models;

namespace SkyCD.Plugin.Metadata.Markdown.Parsing;

/// <summary>
/// Collects the document's leading blocks as drawable lines, with headings flagged for emphasis.
/// </summary>
/// <remarks>
/// Top-level blocks are walked in document order so the page shows what a reader sees first, not what the
/// syntax tree happens to store first. Both a block count and a character count bound the collection, because
/// a document can be short in blocks and enormous in characters, or the other way round.
/// </remarks>
internal static class MarkdownPreviewContentReader
{
    public static IReadOnlyList<MarkdownPreviewLine> Build(
        string text,
        MarkdownPipeline pipeline,
        MarkdownPluginOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(options);

        // Front matter is metadata, not page content: the page starts where the document body starts.
        var frontMatter = MarkdownFrontMatterReader.Read(
            text, options.MaxFrontMatterCharacters, options.MaxMetadataValueCharacters);
        var document = MarkdigMarkdown.Parse(frontMatter.Body, pipeline);

        var lines = new List<MarkdownPreviewLine>();
        var characters = 0;

        foreach (var block in document)
        {
            if (lines.Count >= options.MaxRenderedBlocks || characters >= options.MaxRenderedCharacters)
            {
                break;
            }

            var (content, isHeading) = ReadBlock(block);
            var condensed = Condense(content);
            if (condensed.Length == 0)
            {
                continue;
            }

            var remaining = options.MaxRenderedCharacters - characters;
            if (condensed.Length > remaining)
            {
                condensed = Truncate(condensed, remaining);
            }

            if (condensed.Length == 0)
            {
                break;
            }

            lines.Add(new MarkdownPreviewLine(condensed, isHeading));
            characters += condensed.Length;
        }

        return lines;
    }

    private static (string Content, bool IsHeading) ReadBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return (InlineText.Extract(heading.Inline), true);

            case ParagraphBlock paragraph:
                return (InlineText.Extract(paragraph.Inline), false);

            // A list parses as a ListBlock whose items each hold their paragraphs, so the walk meets the list
            // rather than the items.
            case ListBlock list:
                return (JoinParagraphs(list), false);

            case QuoteBlock quote:
                return (JoinParagraphs(quote), false);

            case CodeBlock code:
                return (code.Lines.ToString(), false);

            default:
                // HtmlBlock, ThematicBreak and other leaf blocks carry no drawable document text.
                return (string.Empty, false);
        }
    }

    private static string JoinParagraphs(ContainerBlock container)
    {
        var builder = new StringBuilder();
        foreach (var child in container)
        {
            switch (child)
            {
                case LeafBlock { Inline: { } inline } leaf:
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(InlineText.Extract(inline));
                    break;

                case CodeBlock code:
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(code.Lines.ToString());
                    break;

                // A deeper section keeps its text; nesting is flattened because the page has no indentation.
                case ContainerBlock nested:
                    AppendNested(nested, builder);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendNested(ContainerBlock container, StringBuilder builder)
    {
        var nested = JoinParagraphs(container);
        if (nested.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(nested);
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
