using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Import;
using SkyCD.Plugin.FileFormat.DiskExplorer.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer;

/// <summary>
/// File format capability plugin for Disk Explorer Professional XML reports
/// (read and write). Detection requires the exact <c>REPORT</c> root plus report
/// shape, so unrelated XML files sharing the .xml extension are never claimed.
/// </summary>
public sealed class DiskExplorerPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "disk-explorer-xml-report",
            DisplayName: "Disk Explorer XML Report (*.xml)",
            Extensions: [".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/vnd.diskexplorer.report+xml"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.ParentId | FileFormatEntryFieldSupport.Kind |
            FileFormatEntryFieldSupport.Size | FileFormatEntryFieldSupport.Properties));

    /// <summary>
    /// Inspects the stream content to verify it is a Disk Explorer XML report.
    /// Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return DiskExplorerRecognizer.IsDiskExplorerXml(stream);
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
                if (request.Source.CanSeek && !DiskExplorerRecognizer.IsDiskExplorerXml(request.Source))
                {
                    throw new Exceptions.DiskExplorerUnsupportedSchemaException();
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
            var parseResult = await Task.Run(
                () => DiskExplorerParser.Parse(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(50);

            var document = DiskExplorerHierarchyBuilder.Compose(parseResult);
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
            await DiskExplorerExporter.ExportAsync(
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
