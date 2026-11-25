using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Parsing;
using SkyCD.Plugin.FileFormat.DiskExplorer.Import;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited;

/// <summary>
/// File format capability plugin for the Disk Explorer Professional
/// delimited-text ("Special Export") profile (read and write). Ambiguous
/// dialects are rejected with actionable diagnostics instead of being
/// guessed; unknown columns import as extension metadata.
/// </summary>
public sealed class DiskExplorerDelimitedPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "disk-explorer-delimited",
            DisplayName: "Disk Explorer Delimited Text (*.tsv, *.csv)",
            Extensions: [".tsv", ".csv", ".txt"],
            MimeTypes: ["text/tab-separated-values", "text/csv", "text/plain"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.ParentId | FileFormatEntryFieldSupport.Kind |
            FileFormatEntryFieldSupport.Size | FileFormatEntryFieldSupport.Properties));

    /// <summary>
    /// Inspects the stream content to verify it is a Disk Explorer
    /// delimited-text report. Preserves the stream position.
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
            return stream.CanSeek && DiskExplorerDelimitedRecognizer.IsDiskExplorerDelimited(stream);
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
                if (request.Source.CanSeek && !DiskExplorerDelimitedRecognizer.IsDiskExplorerDelimited(request.Source))
                {
                    throw new Exceptions.DiskExplorerDelimitedAmbiguousException(
                        "content was not recognized as a Disk Explorer delimited-text report: no header row with known column labels found.");
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
            var import = await Task.Run(
                () => DiskExplorerDelimitedParser.Parse(request.Source, request.FileName, cancellationToken),
                cancellationToken);
            request.Progress?.Report(50);

            var document = DiskExplorerHierarchyBuilder.Compose(import.Result);
            if (import.IsPartial)
            {
                document.Summary.Notes.Add(
                    "Import is partial: entries without explicit path evidence were attached flat under their volume.");
            }

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
            await DiskExplorerDelimitedExporter.ExportAsync(
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
