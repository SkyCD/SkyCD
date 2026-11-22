using System;

namespace SkyCD.Plugin.Metadata.Audio.Models;

/// <summary>
/// Bounds the audio plugin applies on top of the host's own limits. The host still owns the authoritative
/// source-read and artifact budgets; these values stop the plugin from reading or emitting more than it can
/// use. Tags live at the head and tail of a file, so the scan window matters far more than the file size.
/// </summary>
public sealed record AudioPluginOptions
{
    public AudioPluginOptions(
        int headerScanBytes = 1024 * 1024,
        int tagScanBytes = 4 * 1024 * 1024,
        long maxCoverArtBytes = 4 * 1024 * 1024,
        int maxPictureSearchAtoms = 512)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headerScanBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagScanBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCoverArtBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPictureSearchAtoms);

        HeaderScanBytes = headerScanBytes;
        TagScanBytes = tagScanBytes;
        MaxCoverArtBytes = maxCoverArtBytes;
        MaxPictureSearchAtoms = maxPictureSearchAtoms;
    }

    /// <summary>
    /// How far into the file to look for the first audio frame header when an ID3v2 tag is absent or when a
    /// container keeps its stream facts after a small header.
    /// </summary>
    public int HeaderScanBytes { get; }

    /// <summary>How large an ID3v2 tag or MP4 metadata subtree the plugin is willing to walk.</summary>
    public int TagScanBytes { get; }

    /// <summary>
    /// Embedded pictures larger than this are reported through <see cref="AudioSchema.HasCoverArt"/> but not
    /// emitted, because the plugin passes cover art through unmodified rather than re-encoding it.
    /// </summary>
    public long MaxCoverArtBytes { get; }

    /// <summary>Atom visit cap for MP4 traversal, so a pathological tree cannot spin.</summary>
    public int MaxPictureSearchAtoms { get; }

    public static AudioPluginOptions Default { get; } = new();
}
