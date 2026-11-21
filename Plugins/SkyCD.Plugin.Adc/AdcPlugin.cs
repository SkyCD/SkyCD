using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Adc.Import;
using SkyCD.Plugin.Adc.Parsing;

namespace SkyCD.Plugin.Adc;

/// <summary>
/// Read-only file format capability for ElcomSoft Advanced Disk Catalog databases
/// (<c>.cat</c>, <c>.adc</c>).
/// </summary>
/// <remarks>
/// The <c>.cat</c> extension is shared with Windows Authenticode security catalogs; those are
/// recognized from their DER-encoded PKCS#7 SignedData header and rejected before format parsing.
/// The decoder is documented in <c>FORMAT.md</c>.
/// </remarks>
public sealed class AdcPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "advanced-disk-catalog",
            DisplayName: "Advanced Disk Catalog (*.cat, *.adc)",
            Extensions: [".cat", ".adc"],
            MimeTypes: ["application/x-advanced-disk-catalog"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Claims a stream only when the file name carries a supported extension (when a name is
    /// available) and the content starts with the Advanced Disk Catalog header. Authenticode
    /// security catalogs are never claimed. Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !AdcDetector.HasSupportedExtension(fileName))
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return AdcDetector.HasCatalogHeader(stream);
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
            request.Progress?.Report(10);
            var catalog = await Task.Run(
                () => AdcReader.Read(request.Source, cancellationToken),
                cancellationToken);
            request.Progress?.Report(80);

            var document = AdcReportBuilder.Build(catalog);
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
            Error = "Advanced Disk Catalog import is read-only."
        });
    }
}
