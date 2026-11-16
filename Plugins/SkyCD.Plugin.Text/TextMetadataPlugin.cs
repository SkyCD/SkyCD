using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Text;

/// <summary>
/// Extracts typed facts and a bounded plain-text excerpt from a text source. The original is never modified.
/// </summary>
/// <remarks>
/// The excerpt is stored as inert text: markup is never interpreted, no HTML or script is parsed or run, and
/// embedded links are never followed. That is structural rather than a policy promise — the plugin holds
/// bytes from a bounded source and no path, so there is nothing to resolve a reference against.
/// </remarks>
public sealed class TextMetadataPlugin : IMediaMetadataCapability
{
    private readonly TextPluginOptions options;

    public TextMetadataPlugin()
        : this(TextPluginOptions.Default)
    {
    }

    public TextMetadataPlugin(TextPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(TextSchema.Extensions, TextSchema.ContentTypes, TextSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var window = await TextSourceReader.ReadWindowAsync(item, source, options, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.6));

        var analysis = TextAnalyzer.Analyze(window.Bytes, window.ScanComplete, item.Extension, options);

        var values = new List<MediaMetadataValue>
        {
            new(TextSchema.Encoding, MediaMetadataType.Text, Text: analysis.EncodingName),
            new(TextSchema.HasByteOrderMark, MediaMetadataType.Boolean, Boolean: analysis.HasByteOrderMark),
            new(TextSchema.LineEndings, MediaMetadataType.Text, Text: analysis.LineEndings),
            new(TextSchema.LineCount, MediaMetadataType.Integer, Integer: analysis.LineCount),
            new(TextSchema.CharacterCount, MediaMetadataType.Integer, Integer: analysis.CharacterCount),
            new(TextSchema.ByteLength, MediaMetadataType.Integer, Integer: window.SourceLength),
            new(TextSchema.TextKind, MediaMetadataType.Text, Text: TextAnalyzer.Classify(item.Extension)),
            new(TextSchema.ScanComplete, MediaMetadataType.Boolean, Boolean: analysis.ScanComplete),
            new(TextSchema.ExcerptTruncated, MediaMetadataType.Boolean, Boolean: analysis.ExcerptTruncated)
        };

        if (analysis.Excerpt.Length > 0)
        {
            values.Add(new MediaMetadataValue(TextSchema.TextExcerpt, MediaMetadataType.Text, Text: analysis.Excerpt));
        }

        if (!string.IsNullOrWhiteSpace(analysis.Title))
        {
            values.Add(new MediaMetadataValue(TextSchema.Title, MediaMetadataType.Text, Text: analysis.Title));
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }
}
