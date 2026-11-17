using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Vvv;

/// <summary>
/// File format capability plugin for importing VVV (Virtual Volumes View)
/// CSV/TSV exports (read-only).
/// </summary>
public sealed class VvvPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "vvv",
            DisplayName: "VVV Export (*.csv)",
            Extensions: [".csv", ".txt", ".tsv"],
            MimeTypes: ["text/csv", "text/tab-separated-values", "text/plain"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a VVV export
    /// with headers. Headerless VVV exports are not auto-claimed but remain
    /// importable by explicit format selection. Preserves the stream position.
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
            return VvvRecognizer.IsVvvExport(stream);
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
            // The parser itself validates the layout (uniform 6/7 columns,
            // header vocabulary or numeric size shape), so explicit format
            // selection also accepts headerless exports the recognizer skips.
            request.Progress?.Report(10);

            var parseResult = await VvvParser.ParseAsync(request.Source, cancellationToken);
            request.Progress?.Report(50);

            var exportName = !string.IsNullOrWhiteSpace(request.FileName)
                ? Path.GetFileNameWithoutExtension(request.FileName)
                : string.Empty;
            var document = VvvHierarchyBuilder.Compose(parseResult, exportName);
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
            Error = "VVV export is not supported. Format is read-only."
        });
    }
}
