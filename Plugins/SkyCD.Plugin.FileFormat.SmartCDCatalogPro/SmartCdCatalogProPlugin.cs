using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Exceptions;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Import;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Models;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Parsing;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro;

/// <summary>
/// Import-only file format plugin for Smart CD Catalog PRO <c>.scdc</c> catalogs. The
/// catalogs are renamed Jet 4 databases; content detection requires both the Jet
/// signature and the expected application schema.
/// </summary>
public sealed class SmartCdCatalogProPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private const long MaxDatabaseBytes = 2L * 1024 * 1024 * 1024;

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "smart-cd-catalog-pro-scdc",
            DisplayName: "Smart CD Catalog PRO Catalog (*.scdc)",
            Extensions: [".scdc"],
            MimeTypes: ["application/vnd.smart-cd-catalog-pro.scdc"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Verifies the Jet 4 signature and the Smart CD Catalog PRO core schema. The stream
    /// position is restored before returning. A database with a valid Jet container but a
    /// different schema is not claimed.
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
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            if (!SmartCdCatalogProRecognizer.HasJet4Signature(stream))
            {
                return false;
            }

            using var temporary = TemporaryDatabase.CopyFrom(
                stream, ".scdc", MaxDatabaseBytes, LimitExceeded);
            return SmartCdCatalogProReader.HasExpectedSchemaAsync(temporary.Path, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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
            request.Progress?.Report(5);
            using var temporary = await TemporaryDatabase.CopyFromAsync(
                    request.Source, ".scdc", MaxDatabaseBytes, LimitExceeded, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using (var headerStream = new FileStream(
                       temporary.Path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                SmartCdCatalogProRecognizer.ThrowIfInvalidHeader(headerStream);
            }

            var snapshot = await SmartCdCatalogProReader.ReadAsync(temporary.Path, request.Progress, cancellationToken)
                .ConfigureAwait(false);
            var rows = SmartCdCatalogProHierarchyBuilder.Build(
                snapshot,
                request.Progress,
                cancellationToken,
                out var summary);

            request.Progress?.Report(100);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = new SmartCdCatalogProReportDocument(rows, summary)
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
            Error = "Smart CD Catalog PRO import is read-only."
        });
    }

    private static Exception LimitExceeded(long maxBytes) =>
        new SmartCdCatalogProInvalidFormatException(
            $"the database exceeds the supported {maxBytes} byte limit");
}
