using System;
using System.Linq;
using SkyCD.Plugin.Ccd.Exceptions;

namespace SkyCD.Plugin.Ccd;

/// <summary>
/// Byte layout of the data track inside a CloneCD IMG file. Track offsets in the descriptor are sector
/// indexes; the IMG file stores every track back to back at the image's sector size.
/// </summary>
internal sealed record CcdDataTrack(long StartOffset, long EndOffset, int SectorSize, int DataOffsetInSector)
{
    /// <summary>Size of the user-data area exposed to the ISO 9660 reader.</summary>
    public const int UserDataSize = 2048;

    /// <summary>Number of whole sectors stored in the track.</summary>
    public long SectorCount => (EndOffset - StartOffset) / SectorSize;

    /// <summary>Length of the user-data view of the track.</summary>
    public long Length => SectorCount * UserDataSize;

    /// <summary>
    /// Resolves the first data track of the image. Audio tracks before it are skipped by honoring their
    /// declared sector index, and the track ends where the next track starts or where the IMG file ends.
    /// </summary>
    public static CcdDataTrack Resolve(CcdDescriptor descriptor, long imageLength)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.DataTracksScrambled)
        {
            throw new CcdImageFormatException("scrambled data tracks are not supported.");
        }

        if (imageLength <= 0)
        {
            throw new CcdImageFormatException("the image file is empty.");
        }

        var tracks = descriptor.Tracks.OrderBy(track => track.Number).ToArray();
        var unit = SelectSectorUnit(tracks);

        long previousSector = 0;
        for (var index = 0; index < tracks.Length; index++)
        {
            var track = tracks[index];
            var startSector = track.Index1 ?? previousSector;
            var startOffset = startSector * unit;
            if (startOffset < 0 || startOffset >= imageLength)
            {
                if (track.Mode is { IsData: true })
                {
                    throw new CcdImageFormatException("a data track starts outside the image file.");
                }

                continue;
            }

            long endOffset;
            if (index + 1 < tracks.Length && tracks[index + 1].Index1 is { } nextSector && nextSector > startSector)
            {
                endOffset = Math.Min(imageLength, nextSector * unit);
            }
            else
            {
                endOffset = imageLength;
            }

            previousSector = startSector + (endOffset - startOffset) / unit;
            if (track.Mode is not { IsData: true } mode)
            {
                continue;
            }

            if (mode.SectorSize != unit)
            {
                throw new CcdImageFormatException("the data track sector size does not match the image layout.");
            }

            var sectorCount = (endOffset - startOffset) / mode.SectorSize;
            if (sectorCount <= 0)
            {
                throw new CcdImageFormatException("the data track is empty.");
            }

            return new CcdDataTrack(startOffset, startOffset + sectorCount * mode.SectorSize, mode.SectorSize,
                mode.DataOffsetInSector);
        }

        throw new CcdImageFormatException("no data track was found.");
    }

    private static int SelectSectorUnit(CcdTrack[] tracks)
    {
        if (tracks.Any(track => track.Mode is { SectorSize: 2352 }))
        {
            return 2352;
        }

        if (tracks.Any(track => track.Mode is { SectorSize: 2336 }))
        {
            return 2336;
        }

        return 2048;
    }
}
