using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using DiscUtils.Iso9660;
using SkyCD.Plugin.FileFormat.Nrg.Parsing;

namespace SkyCD.Plugin.FileFormat.Nrg.Tests;

/// <summary>Builds ISO 9660 images and wraps them in synthetic NRG containers for tests.</summary>
internal static class NrgImageBuilder
{
    public const int DataOffsetMode3 = 8;
    public const int DataOffsetMode5 = 16;
    public const int DataOffsetMode6 = 24;

    public static byte[] BuildIso()
    {
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddFile(@"ROOT\DEEP\FIXTURE.TXT", Encoding.UTF8.GetBytes("nrg fixture content"));
        builder.AddFile(@"ROOT\README.TXT", Encoding.UTF8.GetBytes("readme"));
        using var stream = new MemoryStream();
        builder.Build(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes <paramref name="iso"/> as a single track laid out with <paramref name="sectorSize"/>-byte sectors
    /// and wraps it in an NRG container using either the version 2 (NER5) or version 1 (NERO) layout.
    /// </summary>
    public static byte[] Create(byte[] iso, int sectorSize = 2048, ushort mode = 0, int version = 2,
        byte[]? trackPrefix = null)
    {
        var data = Repack(iso, sectorSize, mode);
        var trackStart = 0;
        if (trackPrefix is { Length: > 0 })
        {
            var prefixed = new byte[trackPrefix.Length + data.Length];
            Buffer.BlockCopy(trackPrefix, 0, prefixed, 0, trackPrefix.Length);
            Buffer.BlockCopy(data, 0, prefixed, trackPrefix.Length, data.Length);
            data = prefixed;
            trackStart = trackPrefix.Length;
        }

        var trackEnd = data.Length;
        var firstChunkOffset = (long)data.Length;
        using var container = new MemoryStream();
        container.Write(data);

        if (version == 2)
        {
            WriteChunk(container, "CUEX", []);
            WriteChunk(container, "DAOX", BuildDaoxPayload(trackStart, trackEnd, sectorSize, mode));
            WriteChunk(container, "ETN2", BuildEtn2Payload(trackStart, trackEnd, sectorSize, mode));
            WriteChunk(container, "END!", []);
            WriteFooter(container, "NER5"u8, version2: true, firstChunkOffset);
        }
        else
        {
            WriteChunk(container, "ETNF", BuildEtnfPayload(trackStart, trackEnd, mode));
            WriteChunk(container, "END!", []);
            WriteFooter(container, "NERO"u8, version2: false, firstChunkOffset);
        }

        return container.ToArray();
    }

    private static byte[] Repack(byte[] iso, int sectorSize, ushort mode)
    {
        if (sectorSize == 2048)
        {
            return iso;
        }

        var dataOffset = mode switch
        {
            0x0300 => DataOffsetMode3,
            0x0600 => DataOffsetMode6,
            _ => DataOffsetMode5
        };

        var sectorCount = iso.Length / NrgDataTrack.UserDataSize;
        var result = new byte[sectorCount * sectorSize];
        for (var sector = 0; sector < sectorCount; sector++)
        {
            Buffer.BlockCopy(iso, sector * NrgDataTrack.UserDataSize, result,
                sector * sectorSize + dataOffset, NrgDataTrack.UserDataSize);
        }

        return result;
    }

    private static byte[] BuildDaoxPayload(int trackStart, int trackEnd, int sectorSize, ushort mode)
    {
        var payload = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(12), (ushort)sectorSize);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(14), mode);
        payload[20] = 1;
        payload[21] = 1;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(26), trackStart);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(34), trackEnd);
        return payload;
    }

    private static byte[] BuildEtn2Payload(int trackStart, int trackEnd, int sectorSize, ushort mode)
    {
        var payload = new byte[32];
        BinaryPrimitives.WriteUInt64BigEndian(payload, (ulong)trackStart);
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8), (ulong)(trackEnd - trackStart));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16), mode);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(28), (uint)sectorSize);
        return payload;
    }

    private static byte[] BuildEtnfPayload(int trackStart, int trackEnd, ushort mode)
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)trackStart);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)(trackEnd - trackStart));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), mode);
        return payload;
    }

    private static void WriteChunk(Stream stream, string id, byte[] payload)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(id, header);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)(header.Length + payload.Length));
        stream.Write(header);
        stream.Write(payload);
    }

    private static void WriteFooter(Stream stream, ReadOnlySpan<byte> magic, bool version2, long firstChunkOffset)
    {
        Span<byte> footer = stackalloc byte[12];
        magic.CopyTo(footer);
        if (version2)
        {
            BinaryPrimitives.WriteUInt64BigEndian(footer[4..], (ulong)firstChunkOffset);
            stream.Write(footer[..12]);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(footer[4..], (uint)firstChunkOffset);
            stream.Write(footer[..8]);
        }
    }
}
