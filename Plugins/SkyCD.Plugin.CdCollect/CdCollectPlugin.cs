using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.CdCollect;

/// <summary>
/// File format capability plugin for importing CDcollect SQLite database catalogs (*.db, *.cdcollect).
/// </summary>
public sealed class CdCollectPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cdcollect-db",
            DisplayName: "CDcollect Catalog (*.db, *.cdcollect)",
            Extensions: [".db", ".cdcollect"],
            MimeTypes: ["application/x-sqlite3", "application/vnd.sqlite3", "application/x-cdcollect"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a CDcollect SQLite database.
    /// Preserves the stream position.
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
            var header = new byte[16];
            var bytesRead = stream.Read(header, 0, header.Length);
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

            // If file extension matches CDcollect known extensions, fast pass
            if (!string.IsNullOrEmpty(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (ext.Equals(".cdcollect", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Verify SQLite database contains 'cds' table if stream can seek
            if (stream.CanSeek)
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"cdcollect_detect_{Guid.NewGuid():N}.db");
                try
                {
                    stream.Position = 0;
                    using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(fs);
                    }

                    using (var conn = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly;Pooling=False"))
                    {
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='cds';";
                        var count = Convert.ToInt32(cmd.ExecuteScalar());
                        return count > 0;
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
                        SqliteConnection.ClearAllPools();
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

            return true;
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
                cancellationToken);

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
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "CDcollect export is not supported. Format is read-only."
        });
    }
}
