using System;
using System.IO;
using SkyCD.Plugin.FileFormat.BinCue.Exceptions;
using SkyCD.Plugin.FileFormat.BinCue.Models;

namespace SkyCD.Plugin.FileFormat.BinCue.Parsing;

/// <summary>
/// The data track of a BIN file: where its sectors start, where the next track starts, and which bytes of
/// each sector hold the 2048-byte user-data area the ISO 9660 filesystem lives in.
/// </summary>
internal sealed record BinCueDataTrack(
    long StartOffset,
    long EndOffset,
    int SectorSize,
    int DataOffsetInSector)
{
    public const int UserDataSize = 2048;

    public long SectorCount => (EndOffset - StartOffset) / SectorSize;

    public long Length => SectorCount * UserDataSize;

    /// <summary>Picks the first data track in the sheet and confirms its FILE is a raw binary.</summary>
    public static (CueTrack Track, CueFile File) SelectData(CueSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        foreach (var track in sheet.Tracks)
        {
            if (track.Mode is not { IsData: true })
            {
                continue;
            }

            var file = sheet.Files[track.FileIndex];
            if (!string.Equals(file.Type, "BINARY", StringComparison.Ordinal)
                && !string.Equals(file.Type, "MOTOROLA", StringComparison.Ordinal))
            {
                throw new BinCueImageFormatException(
                    $"the data track is stored in a '{file.Type}' file that is not supported.");
            }

            return (track, file);
        }

        throw new BinCueImageFormatException("the CUE sheet contains no data track.");
    }

    /// <summary>Maps a data track onto the BIN file, using the following track as the end of the track.</summary>
    public static BinCueDataTrack Map(CueSheet sheet, CueTrack track, long imageLength)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(track);

        var mode = track.Mode
                   ?? throw new BinCueImageFormatException("the data track mode is not supported.");
        if (track.Index1 is not { } index)
        {
            throw new BinCueImageFormatException("the data track has no INDEX 01.");
        }

        if (imageLength <= 0)
        {
            throw new BinCueImageFormatException("the BIN file is empty.");
        }

        var start = index * mode.SectorSize;
        if (start >= imageLength)
        {
            throw new BinCueImageFormatException("the data track starts outside the BIN file.");
        }

        var end = imageLength;
        foreach (var other in sheet.Tracks)
        {
            if (other.FileIndex != track.FileIndex || other.Index1 is not { } otherIndex ||
                otherIndex <= index)
            {
                continue;
            }

            end = Math.Min(end, otherIndex * mode.SectorSize);
        }

        if (end <= start || (end - start) / mode.SectorSize <= 0)
        {
            throw new BinCueImageFormatException("the data track is empty.");
        }

        return new BinCueDataTrack(start, end, mode.SectorSize, mode.DataOffsetInSector);
    }
}
