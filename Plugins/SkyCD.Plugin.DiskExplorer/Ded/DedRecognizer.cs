using System;
using System.IO;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Content detection for DED databases: <c>DEDB</c> magic, a known format
/// version, and an offset table that fits inside the stream. Arbitrary data that
/// merely uses the <c>.ded</c> extension is not claimed.
/// </summary>
public static class DedRecognizer
{
    public static bool IsDiskExplorerDatabase(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length < DedFormat.HeaderSize)
        {
            return false;
        }

        var initialPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var header = new byte[DedFormat.HeaderSize];
            var total = 0;
            while (total < header.Length)
            {
                var read = stream.Read(header, total, header.Length - total);
                if (read == 0)
                {
                    return false;
                }

                total += read;
            }

            for (var i = 0; i < DedFormat.Magic.Length; i++)
            {
                if (header[i] != DedFormat.Magic[i])
                {
                    return false;
                }
            }

            if (ReadUInt32(header, 0x04) != DedFormat.CurrentVersion)
            {
                return false;
            }

            var tableCount = ReadUInt32(header, 0x08);
            var populatedCount = ReadUInt32(header, 0x0C);
            if (populatedCount > tableCount || tableCount > (uint)new DedReadOptions().MaxTableEntries)
            {
                return false;
            }

            var tableEnd = (ulong)DedFormat.HeaderSize + ((ulong)tableCount * 4UL);
            return tableEnd <= (ulong)stream.Length;
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = initialPosition;
        }
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24));
}
