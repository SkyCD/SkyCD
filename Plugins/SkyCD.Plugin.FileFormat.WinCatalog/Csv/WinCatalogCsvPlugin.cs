using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Csv;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Exceptions;
using SkyCD.Plugin.FileFormat.Csv.Parsing;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Import;
using SkyCD.Plugin.FileFormat.WinCatalog.Csv.Models;
using SkyCD.Plugin.FileFormat.WinCatalog.Models;
using SkyCD.Plugin.EncodingProfiles;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Csv;

/// <summary>
/// File format capability plugin for importing WinCatalog CSV reports.
/// </summary>
public sealed class WinCatalogCsvPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly string[] GenericCsvHeaders = ["NodeId", "ParentId", "Kind", "Name", "SizeBytes"];

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "wincatalog-csv",
            DisplayName: "WinCatalog CSV Report",
            Extensions: [".csv"],
            MimeTypes: ["text/csv", "application/vnd.wincatalog.csv"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a WinCatalog CSV report.
    /// Preserves the stream position.
    /// </summary>
    /// <remarks>
    /// The report is decoded with the same profile resolution the import uses, so a report is never recognised
    /// under one encoding and imported under another.
    /// </remarks>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var data = ReadAllBytes(stream);
            var encoding = CharsetEncodingDetector.Detect(data);
            if (encoding is null)
            {
                return false;
            }

            var text = CharsetEncodingDetector.Decode(encoding, data);
            var headerLine = ReadFirstNonEmptyLine(text);
            if (headerLine is null)
            {
                return false;
            }

            var delimiter = CsvParser.DetectDelimiter(headerLine);
            var headerFields = CsvParser.ParseLine(headerLine, delimiter);
            if (headerFields.Count == 0)
            {
                return false;
            }

            // Must NOT match SkyCD generic CSV
            if (GenericCsvHeaders.SequenceEqual(headerFields, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var hasCatalogPath = false;
            var hasFilePath = false;
            var otherWinCatalogColumnsCount = 0;

            foreach (var field in headerFields)
            {
                var col = WinCatalogProfile.MapColumn(field);
                switch (col)
                {
                    case WinCatalogColumn.CatalogPath:
                        hasCatalogPath = true;
                        break;
                    case WinCatalogColumn.FilePath:
                        hasFilePath = true;
                        break;
                    case WinCatalogColumn.Disk:
                    case WinCatalogColumn.VolumeLabel:
                    case WinCatalogColumn.Comments:
                    case WinCatalogColumn.Tags:
                    case WinCatalogColumn.Rating:
                    case WinCatalogColumn.MD5:
                    case WinCatalogColumn.SHA256:
                    case WinCatalogColumn.Blake2:
                    case WinCatalogColumn.CRC32:
                    case WinCatalogColumn.PhysicalLocation:
                    case WinCatalogColumn.Ext:
                    case WinCatalogColumn.Size:
                    case WinCatalogColumn.Modified:
                    case WinCatalogColumn.Created:
                        otherWinCatalogColumnsCount++;
                        break;
                }
            }

            if (hasCatalogPath)
            {
                return true;
            }

            if (hasFilePath && otherWinCatalogColumnsCount >= 1)
            {
                return true;
            }

            return false;
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

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await ReadAllBytesAsync(request.Source, cancellationToken);
            var encoding = CharsetEncodingDetector.Detect(data);
            if (encoding is null)
            {
                throw new WinCatalogUnsupportedEncodingException();
            }

            request.Progress?.Report(10);

            var text = CharsetEncodingDetector.Decode(encoding, data);

            using var reader = new StringReader(text);
            var parseResult = await CsvParser.ReadCsvAsync(reader, null, cancellationToken);
            request.Progress?.Report(50);

            if (parseResult.Rows.Count == 0)
            {
                throw new WinCatalogCorruptHeaderException();
            }

            var document = WinCatalogHierarchyBuilder.Build(
                parseResult.Rows, parseResult.Delimiter, encoding.EncodingName);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
        }
        catch (DecoderFallbackException)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = new WinCatalogUnsupportedEncodingException().Message
            };
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "WinCatalog CSV export is not supported. Format is read-only."
        });
    }

    private static string? ReadFirstNonEmptyLine(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
