using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.MyCatalogue.Exceptions;
using SkyCD.Plugin.FileFormat.MyCatalogue.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.FileFormat.MyCatalogue;

public sealed class MyCataloguePlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private const long MaxDatabaseBytes = 2L * 1024 * 1024 * 1024;

    public FileFormatDescriptor SupportedFormat => new("mycatalogue-mdb", "MyCatalogue catalog (*.mdb)",
        [".mdb"], ["application/x-msaccess"], CanRead: true, CanWrite: false);

    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek) return false;
        var position = stream.Position;
        TemporaryDatabase? temporary = null;
        try
        {
            stream.Position = 0;
            if (stream.Length > MaxDatabaseBytes) return false;
            if (stream is FileStream file)
            {
                MyCatalogueReader.Validate(file.Name);
            }
            else
            {
                temporary = TemporaryDatabase.CopyFrom(
                    stream, ".mdb", MaxDatabaseBytes, LimitExceeded);
                MyCatalogueReader.Validate(temporary.Path);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            temporary?.Dispose();
            stream.Position = position;
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Source.CanSeek) request.Source.Position = 0;
            using var temporary = await TemporaryDatabase.CopyFromAsync(
                request.Source, ".mdb", MaxDatabaseBytes, LimitExceeded, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            request.Progress?.Report(10);
            var rows = await Task.Run(() => MyCatalogueReader.Read(temporary.Path, request.Progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            request.Progress?.Report(100);
            return new FileFormatReadResult { Success = true, Payload = rows };
        }
        catch (OperationCanceledException) { throw; }
        catch (MyCatalogueImportException exception)
        {
            return new FileFormatReadResult { Success = false, Error = exception.Message };
        }
        catch (Exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = "Invalid or unsupported MyCatalogue catalog: the database could not be read."
            };
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "MyCatalogue import is read-only." });

    private static Exception LimitExceeded(long _) =>
        new MyCatalogueImportException("the database exceeds the 2 GiB limit.");
}
