using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes typed project and asset facts for game engine files: family, format and the counts, names and
/// sizes each engine records. Only the bytes the host exposes are read, bounded per format, and no
/// referenced asset, script or texture is ever opened.
/// </summary>
/// <remarks>
/// A damaged or foreign file that still matches its extension's signature yields <c>Valid=false</c>;
/// content that does not match the extension at all yields no result. Both cases leave the rest of the
/// cataloguing pass untouched.
/// </remarks>
public sealed class GameDevMetadataPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 10;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.Extensions, GameDevSchema.ContentTypes, GameDevSchema.Metadata,
            Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var facts = await GameDevReader.ReadAsync(item, source, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.9));

        List<MediaMetadataValue> values = facts is null ? [] : GameDevValueProjection.Build(facts);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }
}
