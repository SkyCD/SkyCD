using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Exceptions;
using SkyCD.Plugin.Metadata.Works.Export;
using SkyCD.Plugin.Metadata.Works.Models;
using SkyCD.Plugin.Metadata.Works.Parsing;

namespace SkyCD.Plugin.Metadata.Works;

/// <summary>
/// Produces a page-shaped thumbnail of a Microsoft Works document without altering it.
/// </summary>
/// <remarks>
/// An embedded preview image is published as-is when the document carries one. Otherwise a bounded sample of
/// the leading body text is drawn as a page. A flat Works 2 file, an unsupported body or an empty sample yields
/// an explicit empty artifact list, so the caller shows "no preview" rather than a broken image. Rendering is
/// delegated to <see cref="IWorksThumbnailRenderer"/> so a missing rasterizer degrades to metadata only, and
/// generation never changes or blocks metadata extraction. Encrypted documents are never decrypted.
/// </remarks>
public sealed class WorksPreviewPlugin : IMediaPreviewCapability
{
    private readonly WorksPluginOptions options;
    private readonly IWorksThumbnailRenderer renderer;

    public WorksPreviewPlugin()
        : this(WorksPluginOptions.Default, new SkiaWorksThumbnailRenderer())
    {
    }

    public WorksPreviewPlugin(WorksPluginOptions options)
        : this(options, new SkiaWorksThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public WorksPreviewPlugin(WorksPluginOptions options, IWorksThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(WorksSchema.Extensions, WorksSchema.ContentTypes, WorksSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Checked before any read: with no rasterizer there is nothing to produce, so the source budget is
        // left untouched and the metadata capability carries the result alone.
        if (!renderer.IsAvailable)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        try
        {
            var preview = await WorksPreviewSourceReader.ReadAsync(item, source, options, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.4));

            if (preview.EmbeddedImage is { Length: > 0 } image && preview.EmbeddedContentType is { } contentType)
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([new MediaPreviewArtifact(contentType, image)]);
            }

            var paragraphs = WorksTextSampleReader.Read(preview.TextBody, preview.IsWideText,
                options.MaxTextCharacters);
            if (paragraphs.Count == 0)
            {
                // A flat file or a body without a readable text run has no honest image to show.
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            var png = await renderer
                .RenderAsync(paragraphs, options.MaxThumbnailDimension, options.MaxTextCharacters,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new MediaProgress(1));

            if (png is null || png.Length == 0)
            {
                return new MediaPreviewResult([]);
            }

            return new MediaPreviewResult([new MediaPreviewArtifact("image/png", png)]);
        }
        catch (Exception exception) when (exception is WorksDecodeException
                                              or WorksAuthenticationRequiredException
                                              or WorksSourceTooLargeException
                                              or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WorksDecodeException("the document structure could not be parsed", exception);
        }
    }
}
