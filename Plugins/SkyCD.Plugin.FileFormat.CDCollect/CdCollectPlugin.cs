using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.CDCollect.Export;
using SkyCD.Plugin.FileFormat.CDCollect.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.FileFormat.CDCollect;

/// <summary>
/// File format capability plugin for importing and exporting CDcollect SQLite database catalogs (*.db, *.cdcollect).
/// </summary>
public sealed class CdCollectPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability, IDataLossAwareFileFormatCapability
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cdcollect-db",
            DisplayName: "CDcollect Catalog (*.db, *.cdcollect)",
            Extensions: [".db", ".cdcollect"],
            MimeTypes: ["application/x-sqlite3", "application/vnd.sqlite3", "application/x-cdcollect"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.Id | FileFormatEntryFieldSupport.ParentId |
            FileFormatEntryFieldSupport.Kind | FileFormatEntryFieldSupport.Size |
            FileFormatEntryFieldSupport.Properties));

    /// <summary>
    /// Inspects the stream content to verify if it represents a CDcollect SQLite database.
    /// Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            stream.Position = 0;
            var header = new byte[16];
            var bytesRead = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (bytesRead < 16)
            {
                return false;
            }

            for (var i = 0; i < 16; i++)
            {
                if (header[i] != SqliteHeader[i])
                {
                    return false;
                }
            }

            // Confirm the catalog schema; a SQLite header alone is not format detection.
            if (stream.CanSeek)
            {
                try
                {
                    stream.Position = 0;
                    using var database = TemporaryDatabase.CopyFrom(stream, ".db");
                    using var conn = new SqliteConnection(
                        $"Data Source={database.Path};Mode=ReadOnly;Pooling=False");
                    conn.Open();
                    return CdCollectParser.HasCatalogSchema(conn);
                }
                catch
                {
                    return false;
                }
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

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = CdCollectParser.Parse(
                request.Source,
                request.Progress,
                cancellationToken,
                request.SourcePath ?? (request.Source as FileStream)?.Name);

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

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CdCollectWriter.Write(request, cancellationToken);
            return Task.FromResult(new FileFormatWriteResult { Success = true });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = exception.Message });
        }
    }
}
