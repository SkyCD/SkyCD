using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Rtf.Export;
using SkyCD.Plugin.Metadata.Rtf.Models;
using SkyCD.Plugin.Metadata.Rtf.Parsing;

namespace SkyCD.Plugin.Metadata.Rtf;

/// <summary>
/// Renders a bounded thumbnail of the document's leading text.
/// </summary>
/// <remarks>
/// Rendering is delegated to <see cref="IRtfThumbnailRenderer"/> so that a missing rasterizer or an
/// undrawable document degrades to a metadata-only result instead of failing the extraction.
/// </remarks>
public sealed class RtfPreviewPlugin : IMediaPreviewCapability
{
    private readonly RtfPluginOptions options;
    private readonly IRtfThumbnailRenderer renderer;

    public RtfPreviewPlugin()
        : this(RtfPluginOptions.Default, new SkiaRtfThumbnailRenderer())
    {
    }

    public RtfPreviewPlugin(RtfPluginOptions options)
        : this(options, new SkiaRtfThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public RtfPreviewPlugin(RtfPluginOptions options, IRtfThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(RtfSchema.Extensions, RtfSchema.ContentTypes, RtfSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!renderer.IsAvailable)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var rtf = await RtfSourceReader.ReadTextAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.4));

        var document = RtfMetadataPlugin.ReadDocument(rtf);
        if (!document.Paragraphs.Any(paragraph => paragraph.Spans.Count > 0 || paragraph.IsBullet))
        {
            // An empty document has no content to draw; absence is the honest empty state.
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var png = await renderer
            .RenderAsync(document, options.MaxThumbnailDimension, options.MaxRenderedCharacters, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(1));

        if (png is null || png.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact("image/png", png)]);
    }
}
