using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Executable.Exceptions;

namespace SkyCD.Plugin.Executable;

/// <summary>
/// Publishes the program's own icon as a bounded PNG preview during indexing.
/// </summary>
/// <remarks>
/// The descriptor is marked as an index-time preview, which is what makes the indexing pass store the icon in
/// the catalog together with the extracted metadata; the browser then shows it without a second request and
/// while the media is offline. A file without icons, a damaged icon, an icon above the byte or pixel bound and
/// a source above <see cref="ExecutablePluginOptions.MaxIconSourceBytes"/> all produce an empty artifact list —
/// the explicit "no icon" state — so the item keeps its type icon and the metadata is never affected.
/// </remarks>
public sealed class ExecutableIconPlugin : IMediaPreviewCapability
{
    /// <summary>The MIME type of the stored icon; the browser decodes PNG through the device decoder.</summary>
    public const string PngContentType = "image/png";

    private const int ProviderPriority = 20;

    private readonly ExecutablePluginOptions options;

    public ExecutableIconPlugin()
        : this(ExecutablePluginOptions.Default)
    {
    }

    public ExecutableIconPlugin(ExecutablePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(ExecutableSchema.Extensions, ExecutableSchema.ContentTypes, ExecutableSchema.Preview,
            Priority: ProviderPriority, IndexTimePreview: true);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length <= 0 || source.Length > options.MaxIconSourceBytes)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var imageSource = new ExecutableSource(source, options.MaxIconSourceBytes);
        try
        {
            var image = await ExecutableImage.ReadAsync(imageSource, cancellationToken).ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.4));
            if (!image.IsPortableExecutable || !image.HasResources)
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            var resources = await ExecutableResources.ReadAsync(image, imageSource, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.6));

            var extraction = await ExecutableIconExtractor
                .ExtractAsync(imageSource, resources, options, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(1));
            return extraction.Png is { Length: > 0 } png
                ? new MediaPreviewResult([new MediaPreviewArtifact(PngContentType, new ReadOnlyMemory<byte>(png))])
                : new MediaPreviewResult([]);
        }
        catch (ExecutableReadException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }
        catch (ExecutableIconException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }
    }
}
