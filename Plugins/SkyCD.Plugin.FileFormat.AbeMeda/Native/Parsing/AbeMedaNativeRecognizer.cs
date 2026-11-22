using System;
using System.Buffers.Binary;
using System.IO;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Parsing;

/// <summary>
/// Content-aware recognizer for native abeMeda/NeoFinder catalogs. Requires both
/// the observed signature and header sentinel so arbitrary files are never
/// claimed, and restores the stream position on every path.
/// </summary>
public static class AbeMedaNativeRecognizer
{
    public static bool IsAbeMedaNative(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.Position;
        try
        {
            if (stream.Length < AbeMedaNativeLayout.MinimumHeaderBytes)
            {
                return false;
            }

            var header = new byte[AbeMedaNativeLayout.MinimumHeaderBytes];
            stream.Position = 0;
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

            if (!header.AsSpan(0, AbeMedaNativeLayout.SignatureLength)
                    .SequenceEqual("DWDRDNBA"u8))
            {
                return false;
            }

            var sentinel = BinaryPrimitives.ReadUInt32LittleEndian(
                header.AsSpan(AbeMedaNativeLayout.SentinelOffset, 4));
            return sentinel == AbeMedaNativeLayout.SentinelValue;
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
}
