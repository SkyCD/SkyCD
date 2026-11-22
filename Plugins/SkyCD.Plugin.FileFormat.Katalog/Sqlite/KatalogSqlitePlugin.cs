using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Katalog.Import;
using SkyCD.Plugin.FileFormat.Katalog.Models;
using SkyCD.Plugin.FileFormat.Katalog.Sqlite.Parsing;

namespace SkyCD.Plugin.FileFormat.Katalog.Sqlite;

/// <summary>
/// File format capability plugin for importing Katalog File-mode SQLite
/// collection databases (read-only). Catalog rows are built through the
/// shared Katalog hierarchy builder, so Memory-mode and File-mode imports
/// produce identical node shapes and stable source-to-target mappings.
/// </summary>
public sealed class KatalogSqlitePlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "katalog-sqlite",
            DisplayName: "Katalog SQLite Collection (*.db)",
            Extensions: [".db", ".sqlite", ".sqlite3", ".db3"],
            MimeTypes: ["application/x-sqlite3", "application/vnd.sqlite3", "application/x-katalog-sqlite"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a Katalog
    /// File-mode SQLite collection. Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return KatalogSqliteRecognizer.IsKatalogSqlite(stream, fileName);
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
            var initialPosition = request.Source.CanSeek ? request.Source.Position : -1;
            try
            {
                if (request.Source.CanSeek &&
                    !KatalogSqliteRecognizer.IsKatalogSqlite(request.Source, request.FileName))
                {
                    throw new Exceptions.KatalogSqliteUnsupportedSchemaException(
                        "not a Katalog File-mode SQLite collection database");
                }
            }
            finally
            {
                if (initialPosition >= 0 && request.Source.CanSeek)
                {
                    request.Source.Position = initialPosition;
                }
            }

            request.Progress?.Report(5);
            var snapshot = await KatalogSqliteReader.ReadAsync(
                request.Source, request.SourcePath, request.Progress, cancellationToken);

            var collectionRoot = !string.IsNullOrWhiteSpace(request.SourcePath)
                ? Path.GetDirectoryName(request.SourcePath) ?? string.Empty
                : Path.GetTempPath();

            var document = KatalogHierarchyBuilder.Build(
                collectionRoot,
                snapshot.Parameters,
                snapshot.Devices,
                snapshot.Storages,
                snapshot.Tags,
                snapshot.Catalogs,
                snapshot.CatalogFolders);

            var diagnostics = snapshot.Diagnostics.Concat(document.Summary.Diagnostics).ToList();
            var summary = document.Summary with { Diagnostics = diagnostics };

            request.Progress?.Report(100);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = new KatalogReportDocument(document.Rows, summary)
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

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Katalog SQLite export is not supported. Format is read-only."
        });
    }
}
