using System;
using System.IO;
using SkyCD.Plugin.SmartCdCatalogPro.Exceptions;

namespace SkyCD.Plugin.SmartCdCatalogPro;

/// <summary>
/// Validates the outer Jet 4 container signature used by Smart CD Catalog PRO
/// <c>.scdc</c> files. Schema checks live in <see cref="SmartCdCatalogProReader"/>.
/// </summary>
public static class SmartCdCatalogProRecognizer
{
    /// <summary>First 20 bytes of every Jet 4 database, including renamed <c>.scdc</c> files.</summary>
    public static ReadOnlySpan<byte> Jet4Signature =>
    [
        0x00, 0x01, 0x00, 0x00,
        0x53, 0x74, 0x61, 0x6E, 0x64, 0x61, 0x72, 0x64, 0x20, 0x4A, 0x65, 0x74, 0x20, 0x44, 0x42,
        0x00
    ];

    private const int VersionOffset = 0x9C;
    private const int HeaderProbeLength = VersionOffset + 3;

    /// <summary>
    /// Returns true when the stream starts with the Jet 4 signature and version text.
    /// The stream position is restored before returning.
    /// </summary>
    public static bool HasJet4Signature(Stream stream)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var header = new byte[HeaderProbeLength];
            var read = ReadFull(stream, header, 0, header.Length);
            return read == header.Length && IsJet4Header(header);
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    public static bool HasJet4Signature(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return HasJet4Signature(stream);
    }

    public static void ThrowIfInvalidHeader(Stream stream)
    {
        if (!HasJet4Signature(stream))
        {
            throw new SmartCdCatalogProInvalidFormatException(
                "the file does not start with the Jet 4 database signature");
        }
    }

    internal static bool IsJet4Header(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderProbeLength)
        {
            return false;
        }

        if (!header[..Jet4Signature.Length].SequenceEqual(Jet4Signature))
        {
            return false;
        }

        return header.Slice(VersionOffset, 3).SequenceEqual("4.0"u8);
    }

    private static int ReadFull(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
