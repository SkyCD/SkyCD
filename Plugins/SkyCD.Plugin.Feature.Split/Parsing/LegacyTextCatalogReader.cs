using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

namespace SkyCD.Plugin.Feature.Split.Parsing;

/// <summary>
/// Reads a legacy text catalog through the <c>legacy-scd</c> format and records the serialized byte length of
/// every entry line, so a part size limit is measured against the bytes a part actually carries.
/// </summary>
public static class LegacyTextCatalogReader
{
    private const string BinarySignature = "# SkyCD File";
    private const byte CarriageReturn = (byte)'\r';
    private const byte LineFeed = (byte)'\n';
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];

    /// <summary>Reads the catalog file, rejecting the legacy binary layout and catalogs without entries.</summary>
    public static async Task<LegacyTextCatalog> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new NotLegacyTextCatalogException($"'{path}' was not found");
        }

        var plugin = new LegacyScdPlugin();
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (await HasBinarySignatureAsync(source, cancellationToken).ConfigureAwait(false))
        {
            throw new NotLegacyTextCatalogException("the file uses the legacy binary layout");
        }

        var read = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = source,
            FormatId = plugin.SupportedFormat.FormatId,
            FileName = Path.GetFileName(path),
            SourcePath = path
        }, cancellationToken).ConfigureAwait(false);
        if (!read.Success)
        {
            throw new LegacyCatalogReadException(read.Error ?? "the reader reported no reason");
        }

        if (read.Payload is not LegacyScdCatalog catalog || catalog.Entries.Count == 0)
        {
            throw new NotLegacyTextCatalogException("the file holds no catalog entries");
        }

        var lineBytes = await MeasureLineBytesAsync(plugin, catalog, cancellationToken).ConfigureAwait(false);
        var totalBytes = 0L;
        foreach (var length in lineBytes)
        {
            totalBytes += length;
        }

        return new LegacyTextCatalog([.. catalog.Entries], lineBytes, totalBytes);
    }

    private static async Task<bool> HasBinarySignatureAsync(Stream source, CancellationToken cancellationToken)
    {
        var signature = new byte[BinarySignature.Length];
        var offset = 0;
        while (offset < signature.Length)
        {
            var read = await source.ReadAsync(signature.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        source.Position = 0;
        return offset == signature.Length
            && Encoding.ASCII.GetString(signature, 0, offset)
                .Equals(BinarySignature, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<int>> MeasureLineBytesAsync(LegacyScdPlugin plugin,
        LegacyScdCatalog catalog, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var written = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = buffer,
            FormatId = plugin.SupportedFormat.FormatId,
            FileName = "measurement.scd",
            Payload = catalog
        }, cancellationToken).ConfigureAwait(false);
        if (!written.Success)
        {
            throw new LegacyCatalogReadException(written.Error ?? "the entry lines could not be measured");
        }

        return SerializedLineLengths(buffer.ToArray());
    }

    private static IReadOnlyList<int> SerializedLineLengths(byte[] bytes)
    {
        var lengths = new List<int>();
        var start = HasPreamble(bytes) ? Utf8Preamble.Length : 0;
        var cursor = start;
        while (cursor < bytes.Length)
        {
            while (cursor < bytes.Length && bytes[cursor] != CarriageReturn && bytes[cursor] != LineFeed)
            {
                cursor++;
            }

            if (cursor < bytes.Length && bytes[cursor] == CarriageReturn)
            {
                cursor++;
            }

            if (cursor < bytes.Length && bytes[cursor] == LineFeed)
            {
                cursor++;
            }

            lengths.Add(cursor - start);
            start = cursor;
        }

        return lengths;
    }

    private static bool HasPreamble(byte[] bytes)
    {
        if (bytes.Length < Utf8Preamble.Length)
        {
            return false;
        }

        for (var index = 0; index < Utf8Preamble.Length; index++)
        {
            if (bytes[index] != Utf8Preamble[index])
            {
                return false;
            }
        }

        return true;
    }
}
