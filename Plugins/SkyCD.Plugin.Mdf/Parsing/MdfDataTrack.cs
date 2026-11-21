using System;
using System.IO;
using SkyCD.Plugin.Mdf.Exceptions;

namespace SkyCD.Plugin.Mdf.Parsing;

/// <summary>
/// Byte layout of the ISO 9660 data area inside an MDF file. Alcohol images store the data session in
/// 2048-byte, 2352-byte (audio-style), or 2336-byte (Mode 2) sectors depending on how the disc was
/// mastered; the layout is detected from the primary volume descriptor.
/// </summary>
internal sealed record MdfDataTrack(long StartOffset, long EndOffset, int SectorSize, int DataOffsetInSector)
{
    /// <summary>Size of the user-data area exposed to the ISO 9660 reader.</summary>
    public const int UserDataSize = 2048;

    private const int VolumeDescriptorSector = 16;
    private const byte PrimaryVolumeDescriptorType = 1;

    private static readonly (int SectorSize, int DataOffsetInSector)[] Layouts =
    [
        (2048, 0),
        (2352, 16),
        (2352, 24),
        (2336, 8)
    ];

    /// <summary>Number of whole sectors stored in the track.</summary>
    public long SectorCount => (EndOffset - StartOffset) / SectorSize;

    /// <summary>Length of the user-data view of the track.</summary>
    public long Length => SectorCount * UserDataSize;

    /// <summary>
    /// Detects the sector layout by looking for the ISO 9660 primary volume descriptor at the first
    /// volume descriptor sector. Images whose data session does not start at the beginning of the file
    /// are reported as unsupported instead of being read at a wrong offset.
    /// </summary>
    public static MdfDataTrack Probe(string imagePath, long imageLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (imageLength <= 0)
        {
            throw new MdfImageFormatException("the image file is empty.");
        }

        using var image = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        foreach (var (sectorSize, dataOffsetInSector) in Layouts)
        {
            var descriptorOffset = ((long)VolumeDescriptorSector * sectorSize) + dataOffsetInSector;
            if (descriptorOffset + UserDataSize > imageLength)
            {
                continue;
            }

            if (!HasPrimaryVolumeDescriptor(image, descriptorOffset))
            {
                continue;
            }

            var sectorCount = imageLength / sectorSize;
            if (sectorCount <= VolumeDescriptorSector)
            {
                continue;
            }

            return new MdfDataTrack(0, sectorCount * sectorSize, sectorSize, dataOffsetInSector);
        }

        throw new MdfImageFormatException(
            "no ISO 9660 primary volume descriptor was found; the image may use an unsupported layout.");
    }

    private static bool HasPrimaryVolumeDescriptor(Stream image, long offset)
    {
        Span<byte> descriptor = stackalloc byte[7];
        image.Position = offset;
        if (image.ReadAtLeast(descriptor, descriptor.Length, throwOnEndOfStream: false) < descriptor.Length)
        {
            return false;
        }

        return descriptor[0] == PrimaryVolumeDescriptorType
               && descriptor[1] == (byte)'C'
               && descriptor[2] == (byte)'D'
               && descriptor[3] == (byte)'0'
               && descriptor[4] == (byte)'0'
               && descriptor[5] == (byte)'1'
               && descriptor[6] == 1;
    }
}
