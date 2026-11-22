using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.MyCatalogue.Exceptions;
using SkyCD.Plugin.FileFormat.MyCatalogue.Parsing;

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
        string? temporary = null;
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
                temporary = CopyToTemporaryAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
                MyCatalogueReader.Validate(temporary);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporary is not null) DeleteTemporary(temporary);
            stream.Position = position;
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Source.CanSeek) request.Source.Position = 0;
            temporary = await CopyToTemporaryAsync(request.Source, cancellationToken).ConfigureAwait(false);
            request.Progress?.Report(10);
            var rows = await Task.Run(() => MyCatalogueReader.Read(temporary, request.Progress, cancellationToken),
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
        finally
        {
            if (temporary is not null) DeleteTemporary(temporary);
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "MyCatalogue import is read-only." });

    private static async Task<string> CopyToTemporaryAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > MaxDatabaseBytes)
            throw new MyCatalogueImportException("the database exceeds the 2 GiB limit.");

        var path = Path.Combine(Path.GetTempPath(), $"skycd-mycatalogue-{Guid.NewGuid():N}.mdb");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var created = false;
        try
        {
            await using var target = new FileStream(path, options);
            created = true;
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > MaxDatabaseBytes)
                    throw new MyCatalogueImportException("the database exceeds the 2 GiB limit.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return path;
        }
        catch
        {
            if (created) DeleteTemporary(path);
            throw;
        }
    }

    private static void DeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
