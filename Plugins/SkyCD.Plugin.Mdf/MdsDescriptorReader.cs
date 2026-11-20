using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Mdf.Exceptions;

namespace SkyCD.Plugin.Mdf;

/// <summary>
/// Reads and validates the fixed header of an Alcohol MDS descriptor, rejecting descriptors that do not
/// describe a media layout this plugin understands.
/// </summary>
internal static class MdsDescriptorReader
{
    private const int SignatureLength = 16;
    private const int VersionLength = 4;
    private const int HeaderLength = SignatureLength + VersionLength + sizeof(ushort) + sizeof(ushort);
    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");

    private static readonly HashSet<string> SupportedVersions = new(StringComparer.Ordinal)
    {
        "1.10",
        "1.20",
        "2.00"
    };

    private static readonly HashSet<ushort> SupportedMedia =
    [
        0x00, // CD-ROM
        0x01, // CD-R
        0x02, // CD-RW
        0x10, // DVD-ROM
        0x11, // DVD-R
        0x12, // DVD+R
        0x13, // DVD-RW
        0x14, // DVD+RW
        0x15, // DVD-RAM
        0x20, // BD-ROM
        0x21, // BD-R
        0x22  // BD-RE
    ];

    public static MdsDescriptor Read(string descriptorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorPath);
        using var stream = new FileStream(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        return Read(stream);
    }

    public static MdsDescriptor Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Span<byte> header = stackalloc byte[HeaderLength];
        if (source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            throw new MdfImageFormatException("the MDS descriptor is truncated.");
        }

        if (!header[..SignatureLength].SequenceEqual(Signature))
        {
            throw new MdfImageFormatException("the MDS descriptor signature is not recognized.");
        }

        var version = Encoding.ASCII.GetString(header.Slice(SignatureLength, VersionLength));
        if (!SupportedVersions.Contains(version))
        {
            throw new MdfImageFormatException($"MDS descriptor version '{version}' is not supported.");
        }

        var mediumType = BinaryPrimitives.ReadUInt16LittleEndian(header[(SignatureLength + VersionLength)..]);
        if (!SupportedMedia.Contains(mediumType))
        {
            throw new MdfImageFormatException($"MDS medium type 0x{mediumType:X2} is not supported.");
        }

        var sessionCount = BinaryPrimitives.ReadUInt16LittleEndian(header[(SignatureLength + VersionLength + sizeof(ushort))..]);
        if (sessionCount is 0 or > 99)
        {
            throw new MdfImageFormatException("the MDS descriptor reports an invalid session count.");
        }

        return new MdsDescriptor(version, mediumType, sessionCount);
    }
}
