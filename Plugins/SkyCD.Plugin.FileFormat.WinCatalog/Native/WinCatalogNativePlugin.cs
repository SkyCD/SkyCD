using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.WinCatalog.Native.Exceptions;
using SkyCD.Plugin.FileFormat.WinCatalog.Native.Import;
using SkyCD.Plugin.FileFormat.WinCatalog.Native.Parsing;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Native;

/// <summary>
/// Read-only file format capability for native WinCatalog catalog databases
/// (<c>.w3cat</c>). Legacy collection profiles (<c>.wcat</c>, <c>.wca</c>,
/// <c>.wcl</c>) are recognized by extension only to produce an actionable
/// diagnostic; they are never parsed as <c>.w3cat</c>.
/// </summary>
public sealed class WinCatalogNativePlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "wincatalog-native",
            DisplayName: "WinCatalog Native Catalog (*.w3cat)",
            Extensions: [".w3cat", ".wcat", ".wca", ".wcl"],
            MimeTypes: ["application/x-sqlite3", "application/vnd.wincatalog.catalog"],
            CanRead: true,
            CanWrite: false);

    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return WinCatalogNativeRecognizer.IsWinCatalogNative(stream, fileName);
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
            WinCatalogNativeRecognizer.ThrowIfLegacyProfile(request.FileName ?? request.SourcePath);

            var initialPosition = request.Source.CanSeek ? request.Source.Position : -1;
            try
            {
                if (request.Source.CanSeek &&
                    !WinCatalogNativeRecognizer.IsWinCatalogNative(request.Source, request.FileName ?? request.SourcePath))
                {
                    throw new WinCatalogNativeUnsupportedSchemaException(
                        "not a WinCatalog native catalog database.");
                }
            }
            finally
            {
                if (initialPosition >= 0 && request.Source.CanSeek)
                {
                    request.Source.Position = initialPosition;
                }
            }

            request.Progress?.Report(10);
            var snapshot = await WinCatalogNativeReader.ReadAsync(
                request.Source, request.SourcePath, request.Progress, cancellationToken);
            request.Progress?.Report(90);

            var fileStem = Path.GetFileNameWithoutExtension(
                request.FileName ?? request.SourcePath ?? "WinCatalog");
            var document = WinCatalogNativeHierarchyBuilder.Build(snapshot, fileStem);
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
            Error = "WinCatalog native export is not supported. Format is read-only."
        });
    }
}
