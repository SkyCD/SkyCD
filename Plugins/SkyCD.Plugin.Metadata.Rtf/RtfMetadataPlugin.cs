using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Formatting.Rtf;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Rtf.Exceptions;
using SkyCD.Plugin.Metadata.Rtf.Models;
using SkyCD.Plugin.Metadata.Rtf.Parsing;

namespace SkyCD.Plugin.Metadata.Rtf;

/// <summary>
/// Extracts the RTF information group, document statistics and a bounded text excerpt.
/// </summary>
/// <remarks>
/// The original is never modified, and the plugin is handed bytes rather than a path, so an embedded
/// reference has nothing to resolve against. A field the document does not carry is omitted rather than
/// published as an empty string, so "unknown" stays distinguishable from "none".
/// </remarks>
public sealed class RtfMetadataPlugin : IMediaMetadataCapability
{
    private readonly RtfPluginOptions options;

    public RtfMetadataPlugin()
        : this(RtfPluginOptions.Default)
    {
    }

    public RtfMetadataPlugin(RtfPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(RtfSchema.Extensions, RtfSchema.ContentTypes, RtfSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rtf = await RtfSourceReader.ReadTextAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.4));

        var info = RtfInfoReader.Read(rtf);
        var document = ReadDocument(rtf);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        var values = new List<MediaMetadataValue>
        {
            new(RtfSchema.Container, MediaMetadataType.Text, Text: "rtf"),
            new(RtfSchema.IsProtected, MediaMetadataType.Boolean, Boolean: info.IsProtected)
        };

        AddInteger(values, RtfSchema.RtfVersion, info.RtfVersion);
        AddInteger(values, RtfSchema.AnsiCodePage, info.AnsiCodePage);
        AddText(values, RtfSchema.Title, info.Title);
        AddText(values, RtfSchema.Subject, info.Subject);
        AddText(values, RtfSchema.Author, info.Author);
        AddText(values, RtfSchema.Manager, info.Manager);
        AddText(values, RtfSchema.Company, info.Company);
        AddText(values, RtfSchema.Operator, info.Operator);
        AddText(values, RtfSchema.Category, info.Category);
        AddText(values, RtfSchema.Keywords, info.Keywords);
        AddText(values, RtfSchema.Comment, info.Comment);
        AddInteger(values, RtfSchema.RevisionNumber, info.RevisionNumber);
        AddInteger(values, RtfSchema.EditingTimeMinutes, info.EditingTimeMinutes);
        AddInteger(values, RtfSchema.PageCount, info.PageCount);
        AddInteger(values, RtfSchema.WordCount, info.WordCount);
        AddInteger(values, RtfSchema.CharacterCount, info.CharacterCount);
        AddInteger(values, RtfSchema.CharacterCountWithSpaces, info.CharacterCountWithSpaces);
        AddTimestamp(values, RtfSchema.CreatedAt, info.CreatedAt);
        AddTimestamp(values, RtfSchema.ModifiedAt, info.ModifiedAt);
        AddTimestamp(values, RtfSchema.LastPrintedAt, info.LastPrintedAt);

        values.Add(new MediaMetadataValue(RtfSchema.ParagraphCount, MediaMetadataType.Integer,
            Integer: document.Paragraphs.Count(paragraph => paragraph.Spans.Count > 0 || paragraph.IsBullet)));

        AddTextExcerpt(values, document, cancellationToken);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    internal static RichTextDocument ReadDocument(string rtf)
    {
        try
        {
            return RtfReader.Read(rtf);
        }
        catch (InvalidRtfFormatException exception)
        {
            throw new RtfDecodeException("the document body could not be parsed", exception);
        }
    }

    private void AddTextExcerpt(List<MediaMetadataValue> values, RichTextDocument document,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var truncated = false;

        foreach (var paragraph in document.Paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = paragraph.PlainText;
            if (text.Length == 0 && !paragraph.IsBullet)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                if (builder.Length >= options.MaxTextExcerptCharacters)
                {
                    truncated = true;
                    break;
                }

                builder.Append('\n');
            }

            var remaining = options.MaxTextExcerptCharacters - builder.Length;
            var full = paragraph.IsBullet ? $"• {text}" : text;
            if (full.Length > remaining)
            {
                builder.Append(full, 0, remaining);
                truncated = true;
                break;
            }

            builder.Append(full);
        }

        if (builder.Length == 0)
        {
            return;
        }

        values.Add(new MediaMetadataValue(RtfSchema.TextExcerpt, MediaMetadataType.Text, Text: builder.ToString()));
        values.Add(new MediaMetadataValue(RtfSchema.TextExcerptTruncated, MediaMetadataType.Boolean,
            Boolean: truncated));
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text.Trim()));
        }
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, int? value)
    {
        if (value.HasValue)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: value.Value));
        }
    }

    private static void AddTimestamp(List<MediaMetadataValue> values, string key, DateTimeOffset? timestamp)
    {
        if (timestamp is { } value)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: value));
        }
    }
}
