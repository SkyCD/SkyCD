using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Text.Export;
using SkyCD.Plugin.Metadata.Text.Models;
using SkyCD.Plugin.Metadata.Text.Parsing;

namespace SkyCD.Plugin.Metadata.Text;

/// <summary>
/// Publishes the bounded excerpt as a <c>text/plain</c> preview artifact and, after it, a rendered page image
/// of the same excerpt as <c>image/png</c>.
/// </summary>
/// <remarks>
/// Re-encoding to UTF-8 without a byte order mark is deliberate: the text artifact's declared type is
/// <c>text/plain</c>, so a consumer renders it as inert text whatever the source was. An HTML or Markdown
/// source therefore reaches the browser as characters to display, never as markup to interpret, and the
/// original's encoding is recorded separately in the metadata schema rather than being smuggled through the
/// artifact.
/// <para>
/// The text artifact stays first because it is what the preview pane shows; the host resolves the page image
/// separately for the browser list thumbnail. A missing rasterizer leaves only the text artifact.
/// </para>
/// </remarks>
public sealed class TextPreviewPlugin : IMediaPreviewCapability
{
    /// <summary>
    /// Declared as the artifact's content type. A charset parameter is intentionally omitted: the payload is
    /// always UTF-8, and stating it once here keeps consumers from guessing per source encoding.
    /// </summary>
    internal const string PreviewContentType = "text/plain";

    internal const string ThumbnailContentType = "image/png";

    private readonly TextPluginOptions options;
    private readonly ITextThumbnailRenderer renderer;

    public TextPreviewPlugin()
        : this(TextPluginOptions.Default, new SkiaTextThumbnailRenderer())
    {
    }

    public TextPreviewPlugin(TextPluginOptions options)
        : this(options, new SkiaTextThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public TextPreviewPlugin(TextPluginOptions options, ITextThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
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

        progress?.Report(new MediaProgress(0.4));

        var analysis = TextAnalyzer.Analyze(window.Bytes, window.ScanComplete, item.Extension, options);

        if (analysis.Excerpt.Length == 0)
        {
            // An empty file has no preview to show; the absence is the honest empty state.
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(analysis.Excerpt);

        var png = renderer.IsAvailable
            ? await renderer.RenderAsync(analysis.Excerpt, options.MaxThumbnailDimension, cancellationToken)
                .ConfigureAwait(false)
            : null;

        progress?.Report(new MediaProgress(1));

        // Text stays first: the pane prefers it, while the host finds the image separately for the list.
        List<MediaPreviewArtifact> artifacts = [new(PreviewContentType, payload)];
        if (png is { Length: > 0 })
        {
            artifacts.Add(new MediaPreviewArtifact(ThumbnailContentType, png));
        }

        return new MediaPreviewResult(artifacts);
    }
}
