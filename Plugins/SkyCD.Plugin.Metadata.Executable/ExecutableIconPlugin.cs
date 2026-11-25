using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Export;
using SkyCD.Plugin.Metadata.Executable.Models;
using SkyCD.Plugin.Metadata.Executable.Parsing;

namespace SkyCD.Plugin.Metadata.Executable;

/// <summary>
/// Publishes the program's own icon as a bounded PNG preview during indexing.
/// </summary>
/// <remarks>
/// Windows images carry their icon in PE resources, macOS <c>.icns</c> containers store standalone PNG or JPEG
/// chunks, and a type 1 AppImage keeps its <c>.DirIcon</c> in the appended ISO 9660 volume; each source is read
/// from the bytes the host exposes, so no path is needed. A plain ELF or Mach-O file has no icon resource at
/// all and therefore yields the explicit "no icon" state, as do a source above
/// <see cref="ExecutablePluginOptions.MaxIconSourceBytes"/>, a damaged container and an icon above the byte or
/// pixel bound. The descriptor is marked as an index-time preview, which is what makes the indexing pass store
/// the icon in the catalog together with the extracted metadata.
/// </remarks>
public sealed class ExecutableIconPlugin : IMediaPreviewCapability
{
    /// <summary>The MIME type of the stored icon; the browser decodes PNG through the device decoder.</summary>
    public const string PngContentType = "image/png";

    private const int ProviderPriority = 20;

    /// <summary>Enough bytes to classify every source: an AppImage signature ends at offset eleven.</summary>
    private const int HeaderBytes = 16;

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
        new(ExecutableSchema.IconExtensions, ExecutableSchema.IconContentTypes, ExecutableSchema.Preview,
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
            var header = await imageSource
                .ReadAsync(0, (int)Math.Min(imageSource.Length, HeaderBytes), cancellationToken)
                .ConfigureAwait(false);

            if (IcnsReader.HasSignature(header))
            {
                var icon = await IcnsReader.TryReadAsync(imageSource, options, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new MediaProgress(1));
                return ExecutableIconPublisher.Publish(icon, options);
            }

            if (AppImageReader.HasSignature(header))
            {
                var icon = await AppImageReader.TryReadAsync(imageSource, options, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new MediaProgress(1));
                return ExecutableIconPublisher.Publish(icon, options);
            }

            // A plain ELF or Mach-O image has no icon resource; only a DOS signature can start a PE image.
            if (!PeContainerProbe.HasDosSignature(header))
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            var png = await ReadPortableExecutableIconAsync(imageSource, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(1));
            return PngResult(png);
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

    private async Task<byte[]?> ReadPortableExecutableIconAsync(ExecutableSource imageSource,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        var image = await ExecutableImage.ReadAsync(imageSource, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.4));
        if (!image.IsPortableExecutable || !image.HasResources)
        {
            return null;
        }

        var resources = await ExecutableResources.ReadAsync(image, imageSource, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        var extraction = await ExecutableIconExtractor
            .ExtractAsync(imageSource, resources, options, cancellationToken)
            .ConfigureAwait(false);
        return extraction.Png;
    }

    private static MediaPreviewResult PngResult(byte[]? png) => png is { Length: > 0 }
        ? new MediaPreviewResult([new MediaPreviewArtifact(PngContentType, new ReadOnlyMemory<byte>(png))])
        : new MediaPreviewResult([]);
}
