using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.OpenOffice.Models;
using SkyCD.Plugin.Metadata.OpenOffice.Parsing;

namespace SkyCD.Plugin.Metadata.OpenOffice;

/// <summary>
/// Publishes the preview picture an OpenDocument or legacy OpenOffice.org package embeds for browsing.
/// </summary>
/// <remarks>
/// OpenOffice and LibreOffice store the preview as <c>Thumbnails/thumbnail.png</c>, so the capability copies those
/// bytes instead of rendering anything. A package without a usable picture, a protected package and an
/// unreadable source all return an empty artifact list, which the host records as an explicit "no preview",
/// so the metadata capability's result is never put at risk by the preview.
/// </remarks>
public sealed class OpenOfficePreviewPlugin : IMediaPreviewCapability
{
    private readonly OpenOfficePluginOptions options;

    public OpenOfficePreviewPlugin()
        : this(OpenOfficePluginOptions.Default)
    {
    }

    public OpenOfficePreviewPlugin(OpenOfficePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(OpenOfficeSchema.Extensions, OpenOfficeSchema.ContentTypes, OpenOfficeSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        byte[] data;
        try
        {
            data = await OpenOfficeSourceReader
                .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A source that cannot be read costs the preview, never the metadata the other capability publishes.
            return new MediaPreviewResult([]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.5));

        OpenOfficeThumbnail? thumbnail;
        try
        {
            thumbnail = OpenOfficeThumbnailReader.Read(data, options, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(1));

        return thumbnail is null
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(thumbnail.ContentType, thumbnail.Data)]);
    }
}
