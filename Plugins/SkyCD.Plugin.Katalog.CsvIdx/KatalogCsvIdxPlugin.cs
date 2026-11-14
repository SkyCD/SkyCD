using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Katalog.CsvIdx.Exceptions;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// File format capability plugin for importing Katalog Memory-mode CSV/IDX collection folders.
/// </summary>
public sealed class KatalogCsvIdxPlugin :
    IFileFormatPluginCapability,
    IContentAwareFileFormatCapability,
    IFolderAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "katalog-csv-idx",
            DisplayName: "Katalog Collection Folder (.csv / .idx)",
            Extensions: [".idx"],
            MimeTypes: ["application/x-katalog-index"],
            CanRead: true,
            CanWrite: false,
            SupportsFolder: true);

    /// <summary>
    /// Inspects directory contents to verify if it represents a Katalog Memory-mode collection folder.
    /// </summary>
    public bool CanHandleFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            var hasDeviceCsv = File.Exists(Path.Combine(folderPath, "device.csv"));
            var hasStorageCsv = File.Exists(Path.Combine(folderPath, "storage.csv"));
            var hasParametersCsv = File.Exists(Path.Combine(folderPath, "parameters.csv"));
            var hasIdxFiles = Directory.EnumerateFiles(folderPath, "*.idx", SearchOption.TopDirectoryOnly)
                .Any(f => !f.EndsWith(".folders.idx", StringComparison.OrdinalIgnoreCase));

            // Must match Katalog Memory mode signatures:
            // Either device.csv + parameters.csv/storage.csv, or .idx catalog files with device.csv
            if ((hasDeviceCsv && (hasStorageCsv || hasParametersCsv)) || (hasDeviceCsv && hasIdxFiles))
            {
                return true;
            }

            // Alternatively, a folder containing .idx catalog files with Katalog headers
            if (hasIdxFiles)
            {
                var firstIdx = Directory.EnumerateFiles(folderPath, "*.idx", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => !f.EndsWith(".folders.idx", StringComparison.OrdinalIgnoreCase));
                if (firstIdx is not null && IsKatalogIdxFile(firstIdx))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inspects stream content to verify if it represents a standalone Katalog .idx catalog file.
    /// Preserves stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead) return false;

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true);

            var firstLine = reader.ReadLine();
            if (firstLine is not null && firstLine.StartsWith("<catalog", StringComparison.OrdinalIgnoreCase))
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
            var folderPath = request.SourcePath;
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                if (!string.IsNullOrWhiteSpace(request.FileName) && Directory.Exists(request.FileName))
                {
                    folderPath = request.FileName;
                }
                else if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
                {
                    folderPath = Path.GetDirectoryName(request.SourcePath);
                }
            }

            request.Progress?.Report(5);

            // If we have a collection folder
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                return await ReadCollectionFolderAsync(folderPath, request.Progress, cancellationToken);
            }

            // Single .idx file stream reading fallback
            return await ReadSingleIdxStreamAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<FileFormatReadResult> ReadCollectionFolderAsync(
        string folderPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(10);

        var paramPath = Path.Combine(folderPath, "parameters.csv");
        var parameters = await KatalogCsvIdxParser.ParseParametersAsync(paramPath, cancellationToken);

        var devicePath = Path.Combine(folderPath, "device.csv");
        var devices = await KatalogCsvIdxParser.ParseDevicesAsync(devicePath, cancellationToken);

        var storagePath = Path.Combine(folderPath, "storage.csv");
        var storages = await KatalogCsvIdxParser.ParseStorageAsync(storagePath, cancellationToken);

        var tagsPath = Path.Combine(folderPath, "tags.csv");
        var tags = await KatalogCsvIdxParser.ParseTagsAsync(tagsPath, cancellationToken);

        progress?.Report(30);

        // Find all .idx files (excluding .folders.idx)
        var allIdxFiles = Directory.EnumerateFiles(folderPath, "*.idx", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".folders.idx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var catalogs = new List<KatalogIdxCatalog>();
        var catalogFolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var catalogStep = allIdxFiles.Count > 0 ? 50.0 / allIdxFiles.Count : 50.0;
        for (var i = 0; i < allIdxFiles.Count; i++)
        {
            var idxFile = allIdxFiles[i];
            cancellationToken.ThrowIfCancellationRequested();

            var cat = await KatalogCsvIdxParser.ParseIdxAsync(idxFile, null, cancellationToken);
            catalogs.Add(cat);

            var foldersIdxPath = Path.ChangeExtension(idxFile, null) + ".folders.idx";
            if (File.Exists(foldersIdxPath))
            {
                var folders = await KatalogCsvIdxParser.ParseFoldersIdxAsync(foldersIdxPath, cancellationToken);
                catalogFolders[cat.CatalogName] = folders;
            }

            progress?.Report(30 + (int)((i + 1) * catalogStep));
        }

        if (devices.Count == 0 && catalogs.Count == 0)
        {
            throw new KatalogEmptyCollectionException($"No Katalog devices or catalog index files found in '{folderPath}'.");
        }

        var document = KatalogHierarchyBuilder.Build(
            folderPath,
            parameters,
            devices,
            storages,
            tags,
            catalogs,
            catalogFolders);

        progress?.Report(100);

        return new FileFormatReadResult
        {
            Success = true,
            Payload = document
        };
    }

    private async Task<FileFormatReadResult> ReadSingleIdxStreamAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var fs = File.Create(tempFile))
            {
                if (request.Source.CanSeek) request.Source.Position = 0;
                await request.Source.CopyToAsync(fs, cancellationToken);
            }

            var cat = await KatalogCsvIdxParser.ParseIdxAsync(tempFile, request.Progress, cancellationToken);
            cat.CatalogName = Path.GetFileNameWithoutExtension(request.FileName ?? "Catalog");

            var document = KatalogHierarchyBuilder.Build(
                string.Empty,
                new Dictionary<string, string>(),
                [],
                [],
                [],
                [cat],
                new Dictionary<string, List<string>>());

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    private static bool IsKatalogIdxFile(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var line = reader.ReadLine();
            return line is not null && line.StartsWith("<catalog", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Katalog CSV/IDX collection export is not supported. Format is read-only."
        });
    }
}
