using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SkyCD.Plugin.AudioCd.Exceptions;

namespace SkyCD.Plugin.AudioCd;

/// <summary>
/// Computes the two disc identifiers the online sources are keyed by. Both take the same table of contents in
/// absolute CD frames, and both are deterministic, so they are fully unit tested against published vectors.
/// </summary>
/// <remarks>
/// MusicBrainz hashes the ASCII hex of: first track (2 digits), last track (2 digits) and 100 four-byte frame
/// offsets (the lead-out first, then each track, then zero padding), then Base64-encodes the SHA-1 digest using
/// the URL-safe alphabet where <c>+</c> becomes <c>.</c>, <c>/</c> becomes <c>_</c> and <c>=</c> becomes <c>-</c>.
/// The freedb/CDDB id sums the decimal digits of every track offset in whole seconds and packs that, the disc
/// playing time in seconds and the track count into one 32-bit value. See the reference in
/// <c>docs/plugins/audio-cd.md</c>.
/// </remarks>
public static class DiscIdCalculator
{
    private const int OffsetSlots = 100;

    /// <summary>Returns the 28-character MusicBrainz disc id.</summary>
    public static string ComputeMusicBrainzDiscId(DiscToc toc)
    {
        ArgumentNullException.ThrowIfNull(toc);
        EnsureValid(toc);

        var builder = new StringBuilder(4 + OffsetSlots * 8);
        builder.Append(toc.FirstTrack.ToString("X2", CultureInfo.InvariantCulture));
        builder.Append(toc.LastTrack.ToString("X2", CultureInfo.InvariantCulture));

        for (var slot = 0; slot < OffsetSlots; slot++)
        {
            var offset = slot switch
            {
                0 => toc.LeadOutFrames,
                var index when index - 1 < toc.TrackOffsetsFrames.Count => toc.TrackOffsetsFrames[index - 1],
                _ => 0
            };
            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        }

        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(builder.ToString()));
        return Convert.ToBase64String(hash).Replace('+', '.').Replace('/', '_').Replace('=', '-');
    }

    /// <summary>Returns the lowercase eight-digit freedb/CDDB disc id.</summary>
    public static string ComputeCddbDiscId(DiscToc toc)
    {
        ArgumentNullException.ThrowIfNull(toc);
        EnsureValid(toc);

        var checksum = 0;
        foreach (var offset in toc.TrackOffsetsFrames)
        {
            checksum += DigitSum(DiscToc.FramesToSeconds(offset));
        }

        var firstSeconds = DiscToc.FramesToSeconds(toc.TrackOffsetsFrames[0]);
        var lastSeconds = DiscToc.FramesToSeconds(toc.LeadOutFrames);
        var totalSeconds = lastSeconds - firstSeconds;
        if (totalSeconds is < 0 or > 0xFFFF)
        {
            throw new AudioCdTocException("The playing time is outside the range the freedb id can encode.");
        }

        var id = ((checksum % 0xFF) << 24) | (totalSeconds << 8) | toc.TrackCount;
        return unchecked((uint)id).ToString("x8", CultureInfo.InvariantCulture);
    }

    private static int DigitSum(int value)
    {
        var sum = 0;
        while (value > 0)
        {
            sum += value % 10;
            value /= 10;
        }

        return sum;
    }

    private static void EnsureValid(DiscToc toc)
    {
        if (!toc.IsValid())
        {
            throw new AudioCdTocException("The table of contents is outside the range both disc id schemes support.");
        }
    }
}
