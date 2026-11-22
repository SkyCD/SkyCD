using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Text.Models;
using SkyCD.Plugin.Metadata.Text.Parsing;

namespace SkyCD.Plugin.Metadata.Text;

/// <summary>
/// Publishes the bounded excerpt as a <c>text/plain</c> preview artifact.
/// </summary>
/// <remarks>
/// Re-encoding to UTF-8 without a byte order mark is deliberate: the artifact's declared type is
/// <c>text/plain</c>, so a consumer renders it as inert text whatever the source was. An HTML or Markdown
/// source therefore reaches the browser as characters to display, never as markup to interpret, and the
/// original's encoding is recorded separately in the metadata schema rather than being smuggled through the
/// artifact.
/// </remarks>
public sealed class TextPreviewPlugin : IMediaPreviewCapability
{
    /// <summary>
    /// Declared as the artifact's content type. A charset parameter is intentionally omitted: the payload is
    /// always UTF-8, and stating it once here keeps consumers from guessing per source encoding.
    /// </summary>
    internal const string PreviewContentType = "text/plain";

    private readonly TextPluginOptions options;

    public TextPreviewPlugin()
        : this(TextPluginOptions.Default)
    {
    }

    public TextPreviewPlugin(TextPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(TextSchema.Extensions, TextSchema.ContentTypes, TextSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var window = await TextSourceReader.ReadWindowAsync(item, source, options, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.6));

        var analysis = TextAnalyzer.Analyze(window.Bytes, window.ScanComplete, item.Extension, options);

        if (analysis.Excerpt.Length == 0)
        {
            // An empty file has no preview to show; the absence is the honest empty state.
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(analysis.Excerpt);
        progress?.Report(new MediaProgress(1));

        return new MediaPreviewResult([new MediaPreviewArtifact(PreviewContentType, payload)]);
    }
}
