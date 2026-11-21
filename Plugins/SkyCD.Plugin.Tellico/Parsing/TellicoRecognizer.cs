using System;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Tellico.Parsing;

/// <summary>
/// Content-aware recognizer for Tellico collections. Native <c>.tc</c>
/// archives are ZIP files whose first entries include <c>tellico.xml</c>
/// (legacy: <c>bookcase.xml</c>); plain exports are XML documents with a
/// <c>tellico</c> root carrying a <c>syntaxVersion</c> attribute. The scan
/// is bounded and never fetches external resources.
/// </summary>
public static class TellicoRecognizer
{
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];
    private const int PeekSize = 131_072;

    private static readonly string[] ZipMarkers = ["tellico.xml", "bookcase.xml"];

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a
    /// Tellico collection. The stream position is preserved for seekable
    /// streams.
    /// </summary>
    public static bool IsTellico(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var header = new byte[4];
            if (ReadFull(stream, header) < 4)
            {
                return false;
            }

            if (header[0] == ZipMagic[0] && header[1] == ZipMagic[1]
                && header[2] == ZipMagic[2] && header[3] == ZipMagic[3])
            {
                return HasZipMarker(stream);
            }

            return HasXmlMarker(stream, header);
        }
        catch
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

    private static bool HasZipMarker(Stream stream)
    {
        var peek = new byte[PeekSize];
        var total = 0;
        while (total < peek.Length)
        {
            var read = stream.Read(peek, total, peek.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        var text = Encoding.ASCII.GetString(peek, 0, total);
        foreach (var marker in ZipMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasXmlMarker(Stream stream, byte[] header)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var prefix = Encoding.ASCII.GetString(header) + (reader.ReadLine() ?? string.Empty);
        for (var i = 0; i < 4 && !prefix.Contains("<tellico", StringComparison.OrdinalIgnoreCase); i++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            prefix += line;
        }

        if (!prefix.Contains("<tellico", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return prefix.Contains("syntaxVersion", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("xmlns", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("http://periapsis.org/tellico", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
