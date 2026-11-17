using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.DiskCatalogMaker.Exceptions;

namespace SkyCD.Plugin.DiskCatalogMaker;

/// <summary>
/// File format capability plugin for importing DiskCatalogMaker text exports.
/// </summary>
public sealed class DiskCatalogMakerPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly string[] GenericCsvHeaders = ["NodeId", "ParentId", "Kind", "Name", "SizeBytes"];

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "diskcatalogmaker-text",
            DisplayName: "DiskCatalogMaker Text Export",
            Extensions: [".txt", ".tsv"],
            MimeTypes: ["text/plain", "text/tab-separated-values"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a DiskCatalogMaker text export.
    /// Preserves the stream position.
    /// </summary>
    /// <remarks>
    /// The export is decoded with the same strict profile resolution the import uses, so an export
    /// is never recognised under one encoding and imported under another. Headers from other catalog
    /// tools (checksums, tags, catalog paths, Disk Explorer structural labels) veto the match so this
    /// plugin does not steal their formats.
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
            var profile = DiskCatalogMakerEncodingDetector.Detect(data);
            if (profile.Kind == DiskCatalogMakerEncodingKind.Unknown)
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

            // DiskCatalogMaker fixes the Name attribute as the first list-view column; exports
            // from other tools (Disk Explorer starts with VOLUME, WinCatalog with Disk) fail here.
            if (DiskCatalogMakerProfile.MapColumn(headerFields[0]) != DiskCatalogMakerColumn.Name)
            {
                return false;
            }

            // Headers distinctive to other catalog tools veto the match
            if (headerFields.Any(field => DiskCatalogMakerProfile.IsVetoHeader(field)))
            {
                return false;
            }

            var hasPath = false;
            var hasName = false;
            var supportingColumnsCount = 0;

            foreach (var field in headerFields)
            {
                switch (DiskCatalogMakerProfile.MapColumn(field))
                {
                    case DiskCatalogMakerColumn.Path:
                        hasPath = true;
                        break;
                    case DiskCatalogMakerColumn.Name:
                        hasName = true;
                        break;
                    case DiskCatalogMakerColumn.Size:
                    case DiskCatalogMakerColumn.Kind:
                    case DiskCatalogMakerColumn.DateModified:
                    case DiskCatalogMakerColumn.DateCreated:
                    case DiskCatalogMakerColumn.Creator:
                    case DiskCatalogMakerColumn.TypeCode:
                    case DiskCatalogMakerColumn.Comments:
                    case DiskCatalogMakerColumn.Label:
                    case DiskCatalogMakerColumn.Disk:
                        supportingColumnsCount++;
                        break;
                }
            }

            return hasPath && hasName && supportingColumnsCount >= 1;
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
            var profile = DiskCatalogMakerEncodingDetector.Detect(data);
            if (profile.Kind == DiskCatalogMakerEncodingKind.Unknown)
            {
                throw new DiskCatalogMakerUnsupportedEncodingException();
            }

            request.Progress?.Report(10);

            var text = profile.Decode(data);

            using var reader = new StringReader(text);
            var parseResult = await CsvParser.ReadCsvAsync(reader, null, cancellationToken);
            request.Progress?.Report(50);
            cancellationToken.ThrowIfCancellationRequested();

            if (parseResult.Rows.Count == 0)
            {
                throw new DiskCatalogMakerCorruptHeaderException();
            }

            var document = DiskCatalogMakerHierarchyBuilder.Build(parseResult.Rows, parseResult.Delimiter, profile);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = new DiskCatalogMakerUnsupportedEncodingException().Message
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
            Error = "DiskCatalogMaker text export is not supported. Format is read-only."
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
