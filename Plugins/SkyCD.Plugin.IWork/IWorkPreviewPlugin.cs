using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OfficeIMO.IWork;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.IWork.Exceptions;
using SkyCD.Plugin.IWork.Models;
using SkyCD.Plugin.IWork.Parsing;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Publishes the raster thumbnail an Apple iWork document carries for browsing and Quick Look.
/// </summary>
/// <remarks>
/// The thumbnail is already stored inside the package, so the capability copies those bytes instead of
/// rendering anything. A protected package encrypts its members and a package may carry no raster preview at
/// all; both cases return an empty artifact list, which the host records as an explicit "no preview".
/// </remarks>
public sealed class IWorkPreviewPlugin : IMediaPreviewCapability
{
    private readonly IWorkPluginOptions options;

    public IWorkPreviewPlugin()
        : this(IWorkPluginOptions.Default)
    {
    }

    public IWorkPreviewPlugin(IWorkPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(IWorkSchema.Extensions, IWorkSchema.ContentTypes, IWorkSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await IWorkSourceReader
            .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.3));

        using var package = IWorkPackage.Open(data, options);
        using var document = IWorkDocumentLoader.TryOpen(package, data, options, cancellationToken);

        var preview = document?.PreferredRasterPreview;
        if (preview?.MediaType is not { } mediaType)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        byte[] bytes;
        try
        {
            bytes = preview.GetBytes();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or InvalidOperationException)
        {
            throw new IWorkDecodeException("the embedded thumbnail could not be read", exception);
        }

        progress?.Report(new MediaProgress(1));

        if (bytes.Length == 0 || bytes.Length > options.MaxPreviewBytes)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact(mediaType, bytes)]);
    }
}
