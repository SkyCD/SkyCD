using System;
using System.Buffers.Binary;
using System.IO;
using SkyCD.Plugin.FileFormat.Dmg.Exceptions;

namespace SkyCD.Plugin.FileFormat.Dmg.Parsing;

/// <summary>
/// The 512-byte UDIF trailer (<c>koly</c> block) at the end of every DMG file. It identifies the image,
/// bounds the property list that describes its blocks, and reports whether the file is one segment of a
/// larger set.
/// </summary>
internal static class DmgHeader
{
    public const int TrailerSize = 512;

    private const uint Signature = 0x6B6F_6C79;
    private const uint SupportedHeaderSize = 512;
    private const int SegmentNumberOffset = 56;
    private const int SegmentCountOffset = 60;
    private const int XmlOffsetOffset = 216;
    private const int XmlLengthOffset = 224;
    private const int SectorCountOffset = 492;

    private static ReadOnlySpan<byte> EncryptedSignature => "encrcdsa"u8;

    /// <summary>
    /// Reads and validates the trailer without trusting the rest of the file, so damaged, encrypted,
    /// and segmented images fail with a specific message before any block data is interpreted.
    /// </summary>
    public static void Validate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The DMG image stream must be seekable.", nameof(stream));
        }

        if (stream.Length < TrailerSize)
        {
            throw new DmgDamagedImageException();
        }

        if (HasEncryptedSignature(stream))
        {
            throw new DmgEncryptedImageException();
        }

        var trailer = new byte[TrailerSize];
        stream.Position = stream.Length - TrailerSize;
        stream.ReadExactly(trailer);
        stream.Position = 0;

        if (ReadUInt32(trailer, 0) != Signature || ReadUInt32(trailer, 8) != SupportedHeaderSize)
        {
            throw new DmgDamagedImageException();
        }

        var xmlOffset = ReadUInt64(trailer, XmlOffsetOffset);
        var xmlLength = ReadUInt64(trailer, XmlLengthOffset);
        if (xmlLength == 0 || xmlLength > int.MaxValue
            || xmlOffset > (ulong)stream.Length - TrailerSize
            || xmlOffset + xmlLength > (ulong)stream.Length - TrailerSize)
        {
            throw new DmgDamagedImageException();
        }

        if (ReadInt64(trailer, SectorCountOffset) <= 0)
        {
            throw new DmgDamagedImageException();
        }

        var segmentCount = ReadUInt32(trailer, SegmentCountOffset);
        if (segmentCount > 1)
        {
            throw new DmgSegmentedImageException((int)segmentCount,
                Math.Max(1, (int)ReadUInt32(trailer, SegmentNumberOffset)));
        }
    }

    private static bool HasEncryptedSignature(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.Position = 0;
        var read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        return read == signature.Length && signature.SequenceEqual(EncryptedSignature);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(buffer[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(buffer[offset..]);

    private static long ReadInt64(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadInt64BigEndian(buffer[offset..]);
}
