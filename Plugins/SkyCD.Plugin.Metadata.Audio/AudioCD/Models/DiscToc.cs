using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Models;

/// <summary>
/// The table of contents of an audio CD in absolute CD frames. Track one starts at frame 150 (the standard
/// two-second lead-in), and the lead-out marks the end of the last audio track; both disc id schemes are derived
/// from exactly these numbers.
/// </summary>
public sealed record DiscToc(int FirstTrack, IReadOnlyList<int> TrackOffsetsFrames, int LeadOutFrames)
{
    /// <summary>The number of audio tracks on the disc.</summary>
    public int TrackCount => TrackOffsetsFrames.Count;

    /// <summary>The number of the last audio track.</summary>
    public int LastTrack => FirstTrack + TrackOffsetsFrames.Count - 1;

    /// <summary>Validates the ranges the disc id algorithms rely on.</summary>
    public bool IsValid()
    {
        if (FirstTrack is < 1 or > 99 || TrackOffsetsFrames.Count == 0 || TrackOffsetsFrames.Count > 99)
        {
            return false;
        }

        var previous = -1;
        foreach (var offset in TrackOffsetsFrames)
        {
            if (offset < 0 || offset <= previous)
            {
                return false;
            }

            previous = offset;
        }

        return LeadOutFrames > TrackOffsetsFrames[^1];
    }

    /// <summary>Converts a frame offset to whole seconds, truncating the remaining frames as both schemes require.</summary>
    public static int FramesToSeconds(int frames) => frames / 75;
}
