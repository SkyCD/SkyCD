using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

public sealed class MediaCatalogScanner(
    IMediaDriveProvider? driveProvider = null,
    IScanFileSystem? fileSystem = null)
{
    private readonly IMediaDriveProvider driveProvider = driveProvider ?? new SystemMediaDriveProvider();
    private readonly IScanFileSystem fileSystem = fileSystem ?? new RealScanFileSystem();

    public IReadOnlyList<CatalogDocument> Scan(
        MediaScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanDetailed(options, progress, cancellationToken).Documents;

    public CatalogScanResult ScanDetailed(
        MediaScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var context = new ScanOperationContext(
            options.Traversal,
            progress,
            cancellationToken,
            fileSystem);

        if (string.IsNullOrWhiteSpace(options.DrivePathOrName))
        {
            throw new MediaDriveNotFoundException(string.Empty);
        }

        var driveInfo = driveProvider.GetDrive(options.DrivePathOrName)
            ?? throw new MediaDriveNotFoundException(options.DrivePathOrName);

        if (!driveInfo.IsReady)
        {
            throw new MediaDriveNotReadyException(options.DrivePathOrName);
        }

        if (!context.FileSystem.DirectoryExists(driveInfo.RootDirectory))
        {
            throw new MediaDriveInaccessibleException(options.DrivePathOrName, "Root directory does not exist or is not accessible");
        }

        var rootName = !string.IsNullOrWhiteSpace(options.MediaName)
            ? options.MediaName.Trim()
            : (!string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                ? driveInfo.VolumeLabel.Trim()
                : driveInfo.DisplayName);

        progress?.ReportStage("Reading media metadata...", stagePercentage: null, overallPercentage: 10);
        progress?.ReportInfo($"Starting media scan: {driveInfo.DisplayName} ({driveInfo.RootDirectory})", itemPath: driveInfo.RootDirectory);

        var documents = new List<CatalogDocument>();
        var rootId = $"media-{Guid.NewGuid():N}";
        var traverser = new FileSystemCatalogTraverser(context);

        var (totalSize, directChildrenCount, rootProperties) = traverser.Traverse(
            driveInfo.RootDirectory,
            rootId,
            options.IncludeSubfolders,
            options.IncludeExtendedInfo,
            documents);

        if (options.IncludeMediaInfo)
        {
            PopulateMediaDriveProperties(rootProperties, driveInfo);
        }

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

    public IReadOnlyList<CatalogDocument> Scan(MediaScanOptions options, IProgress<(string Status, int Percentage)> progress) =>
        Scan(options, progress is not null ? new OperationProgressAdapter(progress) : null);

    private static void PopulateMediaDriveProperties(PropertiesCollection properties, MediaDriveInfo drive)
    {
        properties["Title"] = drive.Name;
        properties["VolumeLabel"] = drive.VolumeLabel ?? string.Empty;
        properties["RootDirectory"] = drive.RootDirectory;
        properties["AvailableFreeSpace"] = drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture);
        properties["TotalFreeSpace"] = drive.TotalFreeSpace.ToString(CultureInfo.InvariantCulture);
        properties["TotalSize"] = drive.TotalSize.ToString(CultureInfo.InvariantCulture);
        properties["DriveType"] = drive.DriveType.ToString();
        if (!string.IsNullOrWhiteSpace(drive.DriveFormat))
        {
            properties["DriveFormat"] = drive.DriveFormat;
        }
    }
}
