using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.EasyDiskCatalog.Models;
using SkyCD.Plugin.EasyDiskCatalog.Parsing;

namespace SkyCD.Plugin.EasyDiskCatalog;

/// <summary>
/// File format capability plugin for importing Easy Disk Catalog Maker 1.7 catalogs
/// (*.edcdb). The legacy System.Data.SQLite password-protected SQLite format is decoded
/// through the isolated SQLite3 Multiple Ciphers codec bundled with this plugin.
/// Databases are always opened read-only and are never migrated, vacuumed or re-keyed.
/// </summary>
public sealed class EasyDiskCatalogPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "edcdb",
            DisplayName: "Easy Disk Catalog Maker Catalog (*.edcdb)",
            Extensions: [".edcdb"],
            MimeTypes: ["application/x-edcdb", "application/x-sqlite3"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify it is an Easy Disk Catalog database.
    /// Preserves the stream position. Never requires the original application.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.Position;
        var tempFile = Path.Combine(Path.GetTempPath(), $"edcdb_detect_{Guid.NewGuid():N}.db");
        try
        {
            stream.Position = 0;
            using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(output);
            }

            try
            {
                using var database = EasyDiskCatalogReader.OpenDatabase(tempFile);
                var tables = database.ReadTableNames();
                return tables.Contains("Disks")
                    && tables.Contains("Folders")
                    && tables.Contains("Files");
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
            }

            if (stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourcePath = request.SourcePath ?? (request.Source as FileStream)?.Name;
            EasyDiskCatalogReportDocument document;
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                // Open the source file directly read-only; the database engine never writes.
                document = EasyDiskCatalogReader.Parse(
                    sourcePath, null, request.Progress, cancellationToken);
            }
            else
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"edcdb_import_{Guid.NewGuid():N}.db");
                try
                {
                    using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        request.Source.CopyTo(output);
                    }

                    document = EasyDiskCatalogReader.Parse(
                        tempFile, null, request.Progress, cancellationToken);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = document
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatWriteResult
        { Success = false, Error = "Easy Disk Catalog Maker catalogs are read-only. Export to another format instead." });
    }
}
