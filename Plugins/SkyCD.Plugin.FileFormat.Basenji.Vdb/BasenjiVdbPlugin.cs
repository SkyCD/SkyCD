using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Basenji.Vdb.Parsing;

namespace SkyCD.Plugin.FileFormat.Basenji.Vdb;

/// <summary>
/// File format capability plugin for importing Basenji .vdb (Volume Database) catalogs.
/// </summary>
public sealed class BasenjiVdbPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "basenji-vdb",
            DisplayName: "Basenji Catalog (*.vdb)",
            Extensions: [".vdb"],
            MimeTypes: ["application/x-basenji", "application/vnd.sqlite3", "application/x-sqlite3"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a Basenji .vdb catalog.
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
            var bytesRead = stream.Read(header, 0, 16);
            if (bytesRead < 16)
            {
                return false;
            }

            for (var i = 0; i < SqliteHeader.Length; i++)
            {
                if (header[i] != SqliteHeader[i])
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".vdb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Inspect the first 64KB for Basenji schema table names
            var peekBuffer = new byte[65536];
            var totalPeeked = stream.Read(peekBuffer, 0, peekBuffer.Length);
            var contentString = Encoding.ASCII.GetString(peekBuffer, 0, totalPeeked);

            return contentString.Contains("DatabaseProperties", StringComparison.OrdinalIgnoreCase) &&
                   contentString.Contains("Volumes", StringComparison.OrdinalIgnoreCase) &&
                   contentString.Contains("Items", StringComparison.OrdinalIgnoreCase);
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
            var document = BasenjiVdbParser.Parse(
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
            Error = "Basenji (.vdb) export is not supported. Format is read-only."
        });
    }
}
