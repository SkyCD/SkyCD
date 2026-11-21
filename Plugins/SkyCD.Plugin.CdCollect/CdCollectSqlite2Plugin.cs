using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.CdCollect.Parsing;

namespace SkyCD.Plugin.CdCollect;

/// <summary>Assembly-discovered, read-only legacy SQLite 2 catalog capability.</summary>
public sealed class CdCollectSqlite2Plugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat => new(
        FormatId: "cdcollect-sqlite2", DisplayName: "Legacy CDCollect SQLite 2 Catalog (cds.db)",
        Extensions: [".db"], MimeTypes: ["application/x-cdcollect-sqlite2"], CanRead: true, CanWrite: false);

    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek) return false;
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[CdCollectSqlite2Source.Signature.Length];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length ||
                !header.SequenceEqual(CdCollectSqlite2Source.Signature)) return false;
            return CdCollectSqlite2Parser.HasCatalogSchema(stream);
        }
        catch (Exception) { return false; }
        finally { stream.Position = position; }
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = CdCollectSqlite2Parser.Parse(request.Source, request.Progress, cancellationToken,
                request.SourcePath ?? (request.Source as FileStream)?.Name);
            return Task.FromResult(new FileFormatReadResult { Success = true, Payload = report });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Task.FromResult(new FileFormatReadResult { Success = false, Error = ex.Message });
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatWriteResult
        { Success = false, Error = "Legacy CDCollect SQLite 2 catalogs are read-only. Export with cdcollect-db to SQLite 3 instead." });
    }
}
