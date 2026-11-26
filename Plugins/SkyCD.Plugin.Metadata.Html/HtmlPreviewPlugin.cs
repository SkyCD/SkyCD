using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Html.Exceptions;
using SkyCD.Plugin.Metadata.Html.Export;
using SkyCD.Plugin.Metadata.Html.Models;
using SkyCD.Plugin.Metadata.Html.Parsing;

namespace SkyCD.Plugin.Metadata.Html;

/// <summary>
/// Renders the document's leading content as a page image and publishes it as <c>image/png</c>.
/// </summary>
/// <remarks>
/// The page is not a rendering of the HTML source: no browser engine runs and nothing is executed, the
/// visible text of the leading blocks is drawn with headings emphasized. The result is a single artifact, so
/// an entry shows a real document thumbnail in the list while the preview pane keeps the readable text
/// excerpt.
/// <para>
/// The descriptor carries a priority above the text plugin's, because the text plugin also matches
/// <c>text/html</c> sources: the pane already prefers text whichever record wins, and the higher priority
/// is what makes the HTML page the list thumbnail. A missing rasterizer or an empty document yields no
/// artifact rather than a failure, because a document without a thumbnail is still a document.
/// </para>
/// </remarks>
public sealed class HtmlPreviewPlugin : IMediaPreviewCapability
{
    private const string ThumbnailContentType = "image/png";

    /// <summary>Above the text plugin's default, so the HTML page wins the list thumbnail over its excerpt.</summary>
    internal const int PreviewPriority = 10;

    private readonly HtmlPluginOptions options;
    private readonly IHtmlThumbnailRenderer renderer;

    public HtmlPreviewPlugin()
        : this(HtmlPluginOptions.Default, new SkiaHtmlThumbnailRenderer())
    {
    }

    public HtmlPreviewPlugin(HtmlPluginOptions options)
        : this(options, new SkiaHtmlThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public HtmlPreviewPlugin(HtmlPluginOptions options, IHtmlThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(HtmlSchema.Extensions, HtmlSchema.ContentTypes, HtmlSchema.Preview,
            Priority: PreviewPriority);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if ((source.Length > 0 ? source.Length : item.Length) <= 0)
        {
            // An empty source is a document with nothing to draw, not an undecodable one.
            return new MediaPreviewResult([]);
        }

        if (item.Length > options.MaxSourceBytes)
        {
            // Oversized is a "no preview" answer. The metadata provider reports the failure; a page image does
            // not need to repeat it.
            return new MediaPreviewResult([]);
        }

        HtmlDocumentData document;
        try
        {
            var text = await HtmlSourceReader
                .ReadTextAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
            document = HtmlDocumentReader.Read(text, options, cancellationToken);
        }
        catch (HtmlSourceTooLargeException)
        {
            return new MediaPreviewResult([]);
        }
        catch (HtmlDecodeException)
        {
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.4));

        var lines = document.Lines;
        cancellationToken.ThrowIfCancellationRequested();

        if (lines.Count == 0)
        {
            // An empty or textless document has no page to draw; absence is the honest empty state.
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.8));

        var png = renderer.IsAvailable
            ? await renderer.RenderAsync(lines, options.MaxThumbnailDimension, cancellationToken).ConfigureAwait(false)
            : null;

        progress?.Report(new MediaProgress(1));

        if (png is null || png.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact(ThumbnailContentType, png)]);
    }
}
