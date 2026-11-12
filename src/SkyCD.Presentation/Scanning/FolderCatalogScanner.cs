using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

public sealed class FolderCatalogScanner(IScanFileSystem? fileSystem = null)
{
    private readonly IScanFileSystem fileSystem = fileSystem ?? new RealScanFileSystem();

    public IReadOnlyList<CatalogDocument> Scan(
        FolderScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanDetailed(options, progress, cancellationToken).Documents;

    public CatalogScanResult ScanDetailed(
        FolderScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var context = new ScanOperationContext(
            options.Traversal,
            progress,
            cancellationToken,
            fileSystem);

        if (string.IsNullOrWhiteSpace(options.FolderPath) || !context.FileSystem.DirectoryExists(options.FolderPath))
        {
            throw new DirectoryToScanNotFoundException(options.FolderPath ?? string.Empty);
        }

        var rootEntry = context.FileSystem.GetDirectoryEntry(options.FolderPath);
        var rootName = !string.IsNullOrWhiteSpace(options.MediaName)
            ? options.MediaName.Trim()
            : rootEntry.Name;

        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = rootEntry.FullPath;
        }

        progress?.ReportStage("Reading source folder...", stagePercentage: null, overallPercentage: 10);
        progress?.ReportInfo($"Starting folder scan: {options.FolderPath}", itemPath: options.FolderPath);

        var documents = new List<CatalogDocument>();
        var rootId = $"folder-{Guid.NewGuid():N}";
        var traverser = new FileSystemCatalogTraverser(context);

        var (totalSize, directChildrenCount, rootProperties) = traverser.Traverse(
            options.FolderPath,
            rootId,
            options.IncludeSubfolders,
            options.IncludeExtendedInfo,
            documents);

        var rootDoc = new CatalogDocument
        {
            Id = rootId,
            Name = rootName,
            ParentId = options.TargetParentId,
            Type = options.RootType,
            Size = totalSize,
            ChildrenCount = directChildrenCount,
            Properties = rootProperties
        };

        documents.Insert(0, rootDoc);
        context.Statistics.IncrementDiscovered();

        progress?.ReportStage("Scanning complete", stagePercentage: 100, overallPercentage: 100);
        progress?.ReportInfo(
            $"Scanning complete. Discovered={context.Statistics.Discovered}, Skipped={context.Statistics.Skipped}, Failed={context.Statistics.Failed}, Documents={documents.Count} ({totalSize} bytes).");

        return new CatalogScanResult
        {
            Documents = documents,
            Statistics = context.Statistics
        };
    }

    public IReadOnlyList<CatalogDocument> Scan(FolderScanOptions options, IProgress<(string Status, int Percentage)> progress) =>
        Scan(options, progress is not null ? new OperationProgressAdapter(progress) : null);
}
