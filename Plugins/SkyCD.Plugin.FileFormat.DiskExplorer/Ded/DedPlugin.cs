using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Import;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded;

/// <summary>
/// File format capability for Disk Explorer Professional databases
/// (<c>.ded</c>). Reads map the typed object graph to host catalog rows and
/// writes emit a deterministic DEDB v0x102 container; unknown records, flags,
/// extensions, and payloads are preserved.
/// </summary>
public sealed class DedPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "disk-explorer-ded",
            DisplayName: "Disk Explorer Professional Database",
            Extensions: [".ded"],
            MimeTypes: ["application/x-diskexplorer-database"],
            CanRead: true,
            CanWrite: true);

    /// <summary>
    /// Claims a stream only when the file name carries the <c>.ded</c>
    /// extension and the content starts with a bounded, known-version DEDB
    /// header. Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fileName)
            && !string.Equals(Path.GetExtension(fileName), ".ded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return DedRecognizer.IsDiskExplorerDatabase(stream);
    }

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request.Progress?.Report(10);
            var database = await Task.Run(
                () => DedReader.Read(request.Source, options: null, cancellationToken: cancellationToken),
                cancellationToken);
            request.Progress?.Report(70);

            var document = DedCatalogMapper.BuildRows(
                database,
                request.FileName ?? request.SourcePath);
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
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public async Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = ResolveDatabase(request.Payload, request.FileName);
            request.Progress?.Report(20);
            await Task.Run(
                () => DedWriter.Write(request.Target, database, cancellationToken),
                cancellationToken);
            request.Progress?.Report(100);

            return new FileFormatWriteResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static DedDatabase ResolveDatabase(object? payload, string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);

        if (payload is DedExportRequest request)
        {
            return BuildExport(request, stem);
        }

        if (payload is DedReportDocument document)
        {
            return document.Database;
        }

        if (payload is DedDatabase database)
        {
            return database;
        }

        var rows = CoerceRows(payload);
        return DedCatalogMapper.ToDatabase(rows, string.IsNullOrWhiteSpace(stem) ? null : stem);
    }

    private static DedDatabase BuildExport(DedExportRequest request, string? fileStem)
    {
        DedDatabase database;
        if (request.Database is not null)
        {
            database = request.VolumeIds is { Count: > 0 } volumeIds
                ? DedVolumeTransfer.SelectVolumes(request.Database, volumeIds, request.Options)
                : DedVolumeTransfer.Copy(request.Database, request.Options);
        }
        else if (request.Rows is not null)
        {
            database = DedCatalogMapper.ToDatabase(request.Rows, request.Title ?? fileStem);
        }
        else
        {
            throw new DedModelException("export request has neither a source database nor catalog rows.");
        }

        if (request.ExistingDatabase is not null)
        {
            database = DedVolumeTransfer.Merge(request.ExistingDatabase, database);
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            database.Title = request.Title!;
        }
        else if (string.IsNullOrWhiteSpace(database.Title) && !string.IsNullOrWhiteSpace(fileStem))
        {
            database.Title = fileStem!;
        }

        return database;
    }

    private static IReadOnlyList<Dictionary<string, object?>> CoerceRows(object? payload)
    {
        if (payload is IReadOnlyList<Dictionary<string, object?>> rows)
        {
            return rows;
        }

        if (payload is IEnumerable<Dictionary<string, object?>> sequence)
        {
            return sequence.ToList();
        }

        if (payload is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            return array.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.Object
                    ? element.EnumerateObject().ToDictionary(
                        property => property.Name,
                        property => (object?)property.Value.Clone(),
                        StringComparer.OrdinalIgnoreCase)
                    : throw new DedModelException("JSON array items must be objects."))
                .ToList();
        }

        throw new DedModelException("write payload must be a DED database, report document, export request, or catalog rows.");
    }
}
