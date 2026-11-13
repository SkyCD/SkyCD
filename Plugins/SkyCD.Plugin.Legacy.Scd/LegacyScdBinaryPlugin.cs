using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Legacy.Scd;

/// <summary>Exports SCD catalogs using the VB6 binary layout.</summary>
public sealed class LegacyScdBinaryPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-scd-binary",
            DisplayName: "SkyCD Legacy Binary Format",
            Extensions: [".scd"],
            MimeTypes: ["application/vnd.skycd.scd"],
            CanRead: false,
            CanWrite: true);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = false, Error = "Binary SCD import is provided by the combined SCD reader." });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Payload is not LegacyScdCatalog catalog)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Payload must be LegacyScdCatalog." });
        }

        try
        {
            LegacyScdBinaryWriter.Write(request.Target, catalog, request.Progress, cancellationToken);
            return Task.FromResult(new FileFormatWriteResult { Success = true });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = exception.Message });
        }
    }
}
