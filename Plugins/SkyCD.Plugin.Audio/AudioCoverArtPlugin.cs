using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Audio;

/// <summary>
/// Publishes the embedded cover art of an audio source as a bounded preview artifact.
/// </summary>
/// <remarks>
/// The picture is passed through exactly as the container stored it, so the original artwork is never
/// re-encoded and no imaging dependency is needed. Art above <see cref="AudioPluginOptions.MaxCoverArtBytes"/>
/// yields no artifact rather than a failure: <see cref="AudioSchema.HasCoverArt"/> still reports that art
/// exists, so a host can say the cover was too large instead of implying there is none. Sources with no
/// embedded picture produce an empty artifact list, which is the explicit "no cover art" state.
/// </remarks>
public sealed class AudioCoverArtPlugin : IMediaPreviewCapability
{
    private readonly AudioPluginOptions options;

    public AudioCoverArtPlugin()
        : this(AudioPluginOptions.Default)
    {
    }

    public AudioCoverArtPlugin(AudioPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(AudioSchema.Extensions, AudioSchema.ContentTypes, AudioSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var probe = await AudioProber.ProbeAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.9));

        var cover = probe.CoverArt;
        if (cover is null || cover.Data.Length == 0 || cover.Data.Length > options.MaxCoverArtBytes)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(1));
        return new MediaPreviewResult([new MediaPreviewArtifact(cover.ContentType, cover.Data)]);
    }
}
