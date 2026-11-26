using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Html.Exceptions;
using SkyCD.Plugin.Metadata.Html.Models;
using SkyCD.Plugin.Metadata.Html.Parsing;

namespace SkyCD.Plugin.Metadata.Html;

/// <summary>
/// Extracts head fields, structure counts and a bounded text excerpt from an HTML document.
/// </summary>
/// <remarks>
/// The source is never modified and external references are never followed: the plugin holds bytes rather
/// than a path, the document is parsed from memory with an untrusted conversion profile, and links become
/// counts and labels instead of fetches. Nothing in the document is executed. Metadata the document does
/// not carry is omitted rather than published as an empty string, so "unknown" stays distinguishable from
/// "none". A binary file that happens to end in <c>.html</c> is reported as a decode error.
/// </remarks>
public sealed class HtmlMetadataPlugin : IMediaMetadataCapability
{
    private readonly HtmlPluginOptions options;

    public HtmlMetadataPlugin()
        : this(HtmlPluginOptions.Default)
    {
    }

    public HtmlMetadataPlugin(HtmlPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(HtmlSchema.Extensions, HtmlSchema.ContentTypes, HtmlSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var text = await HtmlSourceReader
            .ReadTextAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.4));

        var document = HtmlDocumentReader.Read(text, options, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        var values = new List<MediaMetadataValue>
        {
            new(HtmlSchema.Container, MediaMetadataType.Text, Text: "html"),
            new(HtmlSchema.HasBaseHref, MediaMetadataType.Boolean, Boolean: document.HasBaseHref),
            new(HtmlSchema.LinkCount, MediaMetadataType.Integer, Integer: document.LinkCount),
            new(HtmlSchema.ImageCount, MediaMetadataType.Integer, Integer: document.ImageCount),
            new(HtmlSchema.ScriptCount, MediaMetadataType.Integer, Integer: document.ScriptCount),
            new(HtmlSchema.WordCount, MediaMetadataType.Integer, Integer: CountWords(document.Text)),
            new(HtmlSchema.CharacterCount, MediaMetadataType.Integer, Integer: document.Text.Length)
        };

        AddText(values, HtmlSchema.Title, document.Title);
        AddText(values, HtmlSchema.Description, document.Description);
        AddText(values, HtmlSchema.Keywords, document.Keywords);
        AddText(values, HtmlSchema.Author, document.Author);
        AddText(values, HtmlSchema.Generator, document.Generator);
        AddText(values, HtmlSchema.Language, document.Language);
        AddText(values, HtmlSchema.DeclaredCharset, document.DeclaredCharset);

        AddTextExcerpt(values, document.Text, cancellationToken);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    private void AddTextExcerpt(List<MediaMetadataValue> values, string text, CancellationToken cancellationToken)
    {
        if (text.Length == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var truncated = text.Length > options.MaxTextExcerptCharacters;
        var length = truncated ? options.MaxTextExcerptCharacters : text.Length;
        if (truncated && char.IsHighSurrogate(text[length - 1]))
        {
            // Do not split a surrogate pair; a lone surrogate is not a valid stored string.
            length--;
        }

        var excerpt = text[..length];
        if (excerpt.Length == 0)
        {
            return;
        }

        values.Add(new MediaMetadataValue(HtmlSchema.TextExcerpt, MediaMetadataType.Text, Text: excerpt));
        values.Add(new MediaMetadataValue(HtmlSchema.TextExcerptTruncated, MediaMetadataType.Boolean,
            Boolean: truncated));
    }

    private void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var value = text.Trim();
        if (value.Length > options.MaxMetadataValueCharacters)
        {
            value = value[..options.MaxMetadataValueCharacters];
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
    }
}
