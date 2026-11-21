using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.WinCatalog.Csv.Exceptions;
using SkyCD.Plugin.Csv.Parsing;
using SkyCD.Plugin.WinCatalog.Csv.Import;
using SkyCD.Plugin.WinCatalog.Csv.Models;
using SkyCD.Plugin.WinCatalog.Csv.Parsing;

namespace SkyCD.Plugin.WinCatalog.Csv;

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
            var profile = WinCatalogCsvEncodingDetector.Detect(data);
            if (profile.Kind == WinCatalogCsvEncodingKind.Unknown)
            {
                return false;
            }

            var text = profile.Decode(data);
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
                var col = WinCatalogCsvProfile.MapColumn(field);
                switch (col)
                {
                    case WinCatalogCsvColumn.CatalogPath:
                        hasCatalogPath = true;
                        break;
                    case WinCatalogCsvColumn.FilePath:
                        hasFilePath = true;
                        break;
                    case WinCatalogCsvColumn.Disk:
                    case WinCatalogCsvColumn.VolumeLabel:
                    case WinCatalogCsvColumn.Comments:
                    case WinCatalogCsvColumn.Tags:
                    case WinCatalogCsvColumn.Rating:
                    case WinCatalogCsvColumn.MD5:
                    case WinCatalogCsvColumn.SHA256:
                    case WinCatalogCsvColumn.Blake2:
                    case WinCatalogCsvColumn.CRC32:
                    case WinCatalogCsvColumn.PhysicalLocation:
                    case WinCatalogCsvColumn.Ext:
                    case WinCatalogCsvColumn.Size:
                    case WinCatalogCsvColumn.Modified:
                    case WinCatalogCsvColumn.Created:
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
            var profile = WinCatalogCsvEncodingDetector.Detect(data);
            if (profile.Kind == WinCatalogCsvEncodingKind.Unknown)
            {
                throw new WinCatalogUnsupportedEncodingException();
            }

            request.Progress?.Report(10);

            var text = profile.Decode(data);

            using var reader = new StringReader(text);
            var parseResult = await CsvParser.ReadCsvAsync(reader, null, cancellationToken);
            request.Progress?.Report(50);

            if (parseResult.Rows.Count == 0)
            {
                throw new WinCatalogCorruptHeaderException();
            }

            var document = WinCatalogHierarchyBuilder.Build(parseResult.Rows, parseResult.Delimiter, profile);
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
