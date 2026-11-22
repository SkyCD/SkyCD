using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Vvv.Exceptions;
using SkyCD.Plugin.FileFormat.Vvv.Import;
using SkyCD.Plugin.FileFormat.Vvv.Parsing;

namespace SkyCD.Plugin.FileFormat.Vvv;

/// <summary>
/// Read-only import of VVV (Virtual Volumes View) logical backups
/// (<c>*.vvvbk</c>). The backup is restored with a private Firebird 5
/// embedded engine into a bounded temporary workspace; the source file and
/// any existing catalog are never modified.
/// </summary>
public sealed class VvvBackupPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "vvv-backup",
            DisplayName: "VVV Backup (*.vvvbk)",
            Extensions: [".vvvbk"],
            MimeTypes: ["application/octet-stream", "application/x-firebird"],
            CanRead: true,
            CanWrite: false);

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Source is null || !request.Source.CanRead)
            {
                throw new VvvBackupMalformedException("the source stream is not readable");
            }

            request.Progress?.Report(5);
            var engine = VvvBackupEngineLocator.Resolve();

            using var workspace = await VvvBackupWorkspace.CreateAsync(engine, request.Source, cancellationToken);
            request.Progress?.Report(25);

            var snapshot = await Task.Run(
                () => VvvBackupReader.Read(workspace, cancellationToken),
                cancellationToken);
            request.Progress?.Report(75);

            var document = VvvBackupHierarchyBuilder.Build(
                snapshot,
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

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "VVV backup import is read-only."
        });
    }
}
