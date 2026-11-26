using System;

namespace SkyCD.Plugin.Metadata.Audio.Models;

/// <summary>
/// Bounds the audio plugin applies on top of the host's own limits. The host still owns the authoritative
/// source-read and artifact budgets; TagLibSharp only asks the source for the ranges a container needs.
/// </summary>
public sealed record AudioPluginOptions
{
    public AudioPluginOptions(long maxCoverArtBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCoverArtBytes);

        MaxCoverArtBytes = maxCoverArtBytes;
    }

    /// <summary>
    /// Embedded pictures larger than this are reported through <see cref="AudioSchema.HasCoverArt"/> but not
    /// emitted, because the plugin passes cover art through unmodified rather than re-encoding it.
    /// </summary>
    public long MaxCoverArtBytes { get; }

    public static AudioPluginOptions Default { get; } = new();
}
