using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Office.Models;
using SkyCD.Plugin.Metadata.Office.Parsing;

namespace SkyCD.Plugin.Metadata.Office;

/// <summary>
/// Publishes the thumbnail a Microsoft Office document embeds for browsing and Quick Look.
/// </summary>
/// <remarks>
/// The thumbnail is already stored inside the document, so the capability copies those bytes instead of
/// rendering anything. An OOXML package carries it as <c>docProps/thumbnail.jpeg</c>; a legacy compound document
/// carries a bitmap in its summary property set. A document without a usable picture, an encrypted package and
/// an unreadable part all return an empty artifact list, which the host records as an explicit "no preview",
/// so the metadata capability's result is never put at risk by the preview.
/// </remarks>
public sealed class OfficePreviewPlugin : IMediaPreviewCapability
{
    private readonly OfficePluginOptions options;

    public OfficePreviewPlugin()
        : this(OfficePluginOptions.Default)
    {
    }

    public OfficePreviewPlugin(OfficePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(OfficeSchema.Extensions, OfficeSchema.ContentTypes, OfficeSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await OfficeSourceReader.ReadSourceAsync(item, source, options, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.5));

        var thumbnail = OfficeThumbnailReader.Read(
            data, OfficeFamilyResolver.Normalize(item.Extension), options, cancellationToken);
        progress?.Report(new MediaProgress(1));

        return thumbnail is null
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(thumbnail.ContentType, thumbnail.Data)]);
    }
}
