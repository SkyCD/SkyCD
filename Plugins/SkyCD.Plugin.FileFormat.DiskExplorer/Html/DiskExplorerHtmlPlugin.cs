using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer.Html.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Html.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html;

/// <summary>
/// File format capability plugin for Disk Explorer Professional HTML reports
/// (read and write). Detection requires report-shaped markup so unrelated HTML
/// files sharing the .htm/.html extension are never claimed.
/// </summary>
public sealed class DiskExplorerHtmlPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "disk-explorer-html-report",
            DisplayName: "Disk Explorer HTML Report (*.htm, *.html)",
            Extensions: [".htm", ".html"],
            MimeTypes: ["text/html"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.ParentId | FileFormatEntryFieldSupport.Kind |
            FileFormatEntryFieldSupport.Size | FileFormatEntryFieldSupport.Properties));

    /// <summary>
    /// Inspects the stream content to verify it is a Disk Explorer HTML report.
    /// Preserves the stream position.
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
            return DiskExplorerHtmlRecognizer.IsDiskExplorerHtml(stream);
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
            var initialPosition = request.Source.CanSeek ? request.Source.Position : -1;
            try
            {
                if (request.Source.CanSeek && !DiskExplorerHtmlRecognizer.IsDiskExplorerHtml(request.Source))
                {
                    throw new Exceptions.DiskExplorerHtmlUnsupportedSchemaException();
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
            var document = await Task.Run(
                () => DiskExplorerHtmlReader.Parse(
                    request.Source, request.SourcePath, request.Progress, cancellationToken),
                cancellationToken);
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

    public async Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DiskExplorerHtmlExporter.ExportAsync(
                request.Target, request.FileName, request.Payload, request.Progress, cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }
}
