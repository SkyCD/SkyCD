using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Office.Exceptions;

namespace SkyCD.Plugin.Office;

/// <summary>
/// Reads the published metadata parts of an OOXML ZIP package.
/// </summary>
/// <remarks>
/// Only <c>docProps/core.xml</c> and <c>docProps/app.xml</c> are decompressed, each bounded by a configured byte
/// cap, and XML parsing prohibits a DTD and never resolves an external resource, so a crafted part cannot make
/// the plugin expand entities or fetch anything. The document part names decide Word, Excel or PowerPoint, which
/// rejects an unrelated ZIP that merely carries an Office extension.
/// </remarks>
internal static class OfficePackageReader
{
    private const int ChunkBytes = 8192;

    private static readonly string[] WordParts = ["word/document.xml"];

    private static readonly string[] ExcelParts = ["xl/workbook.xml", "xl/workbook.bin"];

    private static readonly string[] PowerPointParts = ["ppt/presentation.xml"];

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'K'
        && (data[2], data[3]) is (0x03, 0x04) or (0x05, 0x06) or (0x07, 0x08);

    public static OfficeMetadata Read(byte[] data, OfficePluginOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var documentType = DetectDocumentType(archive)
                ?? throw new OfficeDecodeException(
                    "the package does not contain a Word, Excel or PowerPoint document part");

            cancellationToken.ThrowIfCancellationRequested();
            var core = OfficeCoreProperties.Read(
                ReadPart(archive, "docProps/core.xml", options.MaxMetadataPartBytes, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            var extended = OfficeExtendedProperties.Read(
                ReadPart(archive, "docProps/app.xml", options.MaxMetadataPartBytes, cancellationToken));

            return OfficeMetadata.FromPackage(documentType, core, extended);
        }
        catch (InvalidDataException exception)
        {
            throw new OfficeDecodeException("the package structure could not be read", exception);
        }
    }

    private static string? DetectDocumentType(ZipArchive archive)
    {
        if (WordParts.Any(name => FindEntry(archive, name) is not null))
        {
            return "word";
        }

        if (ExcelParts.Any(name => FindEntry(archive, name) is not null))
        {
            return "excel";
        }

        return PowerPointParts.Any(name => FindEntry(archive, name) is not null) ? "powerpoint" : null;
    }

    private static XDocument? ReadPart(ZipArchive archive, string name, int maxBytes, CancellationToken cancellationToken)
    {
        var entry = FindEntry(archive, name);
        if (entry is null || entry.Length <= 0 || entry.Length > maxBytes)
        {
            return null;
        }

        var buffer = ReadBounded(entry, maxBytes, cancellationToken);
        if (buffer is null)
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                MaxCharactersInDocument = maxBytes
            };

            using var reader = XmlReader.Create(new MemoryStream(buffer, writable: false), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            // A damaged optional part costs its fields, not the whole extraction.
            return null;
        }
    }

    /// <summary>Decompresses a part, refusing to hold more than the configured cap even if the entry lies about its size.</summary>
    private static byte[]? ReadBounded(ZipArchiveEntry entry, int maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream(Math.Min(maxBytes, ChunkBytes * 4));
            var chunk = new byte[ChunkBytes];
            int read;
            while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffer.Length + read > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            // A CRC or deflate failure is a damaged part, handled like an absent one.
            return null;
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));
}
