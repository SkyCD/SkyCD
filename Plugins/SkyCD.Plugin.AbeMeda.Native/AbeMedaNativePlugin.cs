using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.AbeMeda.Native.Import;
using SkyCD.Plugin.AbeMeda.Native.Parsing;

namespace SkyCD.Plugin.AbeMeda.Native;

/// <summary>
/// Read-only file format capability for native abeMeda/NeoFinder catalogs.
/// The classic <c>.cdw</c> container is parsed at the verified header/record
/// layer; the extended <c>.abeMeda7</c>/<c>.neofinder7</c> generation is
/// recognized but gated until genuine fixtures verify its layout.
/// </summary>
public sealed class AbeMedaNativePlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "abemeda-native",
            DisplayName: "abeMeda/NeoFinder Native Catalog (*.cdw, *.abeMeda7, *.neofinder7)",
            Extensions: [".cdw", ".abemeda7", ".neofinder7"],
            MimeTypes: ["application/vnd.abemeda.catalog", "application/vnd.neofinder.catalog"],
            CanRead: true,
            CanWrite: false);

    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.Position;
        try
        {
            return AbeMedaNativeRecognizer.IsAbeMedaNative(stream);
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = initialPosition;
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var source = request.Source;
            request.Progress?.Report(10);
            var name = request.FileName ?? request.SourcePath;
            var catalog = await Task.Run(
                () => AbeMedaNativeCatalogReader.Read(source, name, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            request.Progress?.Report(85);

            var document = AbeMedaNativeHierarchyBuilder.Build(catalog, ResolveFileStem(name));
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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "abeMeda/NeoFinder native catalog export is not supported. Format is read-only."
        });
    }

    private static string ResolveFileStem(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return "abeMeda Catalog";
        }

        var stem = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim());
        return string.IsNullOrWhiteSpace(stem) ? "abeMeda Catalog" : stem;
    }
}
