using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Export;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Generates an on-demand structure page for project and scene files: family, format, sizes, versions, counts
/// and the collected node, class or layout names.
/// </summary>
/// <remarks>
/// The page is not produced during indexing because it duplicates facts the metadata extraction already
/// stores; it is drawn only when a caller asks for a larger view. A file that does not match its extension
/// yields no artifact.
/// </remarks>
public sealed class GameDevStructurePreviewPlugin : IMediaPreviewCapability, IMediaFormatProbe
{
    internal const string ThumbnailContentType = "image/png";

    private const int PreviewPriority = 30;

    private readonly GameDevStructurePageRenderer renderer = new();

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.StructureExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
            Priority: PreviewPriority);

    public ValueTask<bool> CanHandleAsync(MediaItem item, IMediaSource source,
        CancellationToken cancellationToken) =>
        GameDevProbe.AcceptsAsync(item, source, cancellationToken);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var facts = await GameDevReader.ReadAsync(item, source, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        if (facts is null || facts.Valid == false)
        {
            return new MediaPreviewResult([]);
        }

        var png = await renderer.RenderAsync(facts, GameDevLimits.MaxThumbnailDimension, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));

        if (png is null || png.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact(ThumbnailContentType, png)]);
    }
}
