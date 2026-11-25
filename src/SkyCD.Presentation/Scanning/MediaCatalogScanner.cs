using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Capabilities.Volume;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Locations;

namespace SkyCD.Presentation.Scanning;

public sealed class MediaCatalogScanner(
    IMediaDriveProvider? driveProvider = null,
    IScanFileSystem? fileSystem = null,
    IVolumeMetadataProvider? volumeMetadata = null)
{
    private readonly IMediaDriveProvider driveProvider = driveProvider ?? new SystemMediaDriveProvider();
    private readonly IScanFileSystem fileSystem = fileSystem ?? new RealScanFileSystem();
    private readonly IVolumeMetadataProvider? volumeMetadataProvider = volumeMetadata;

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

        // An audio CD has no filesystem, so on Unix the drive is addressed by its device node ("/dev/sr0",
        // "/dev/rdiskN"). The disc is catalogued as a single media root; its metadata is captured afterwards.
        var hasFileSystem = context.FileSystem.DirectoryExists(driveInfo.RootDirectory);
        if (!hasFileSystem && !IsOpticalDeviceRoot(driveInfo))
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

        long totalSize = 0;
        int directChildrenCount = 0;
        var rootProperties = new PropertiesCollection();
        if (hasFileSystem)
        {
            (totalSize, directChildrenCount, rootProperties) = traverser.Traverse(
                driveInfo.RootDirectory,
                rootId,
                options.IncludeSubfolders,
                options.IncludeFiles,
                options.IncludeExtendedInfo,
                documents);
        }

        if (options.IncludeMediaInfo)
        {
            PopulateMediaDriveProperties(rootProperties, driveInfo);
            PopulateVolumeMetadata(rootProperties, driveInfo, cancellationToken);
        }
        else if (!hasFileSystem)
        {
            // These two keys identify the drive for media metadata capture; without a filesystem they are
            // the only facts the entry can carry, so they do not depend on the optional media-info setting.
            rootProperties["RootDirectory"] = driveInfo.RootDirectory;
            rootProperties["DriveType"] = driveInfo.DriveType.ToString();
        }

        if (!string.IsNullOrWhiteSpace(options.LocationId))
        {
            rootProperties[CatalogLocationService.LocationPropertyKey] = options.LocationId.Trim();
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

    private static bool IsOpticalDeviceRoot(MediaDriveInfo drive) =>
        drive.DriveType == DriveType.CDRom &&
        drive.RootDirectory.Trim().StartsWith("/dev/", StringComparison.Ordinal);

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

    /// <summary>
    /// Adds optional volume facts from enabled volume-aware plugins. Capture is best-effort: missing or
    /// failed values are simply not written, so a later refresh can never erase a previously captured
    /// serial with a fabricated placeholder, and an unexpected failure never breaks the scan.
    /// </summary>
    private void PopulateVolumeMetadata(PropertiesCollection properties, MediaDriveInfo drive,
        CancellationToken cancellationToken)
    {
        if (volumeMetadataProvider is null)
        {
            return;
        }

        try
        {
            var values = volumeMetadataProvider.CaptureVolumeMetadata(
                new VolumeMediaContext(drive.RootDirectory, drive.Name, drive.VolumeLabel, drive.DriveFormat),
                cancellationToken);
            foreach (var value in values)
            {
                var text = value.Type switch
                {
                    MediaMetadataType.Text => value.Text,
                    MediaMetadataType.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture),
                    MediaMetadataType.Number => value.Number?.ToString(CultureInfo.InvariantCulture),
                    MediaMetadataType.Boolean => value.Boolean?.ToString(),
                    MediaMetadataType.Timestamp => value.Timestamp?.ToString("O", CultureInfo.InvariantCulture),
                    _ => null
                };
                if (text is not null)
                {
                    properties[value.Key] = text;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Volume capture is optional enrichment; plugin failures are reported at the provider boundary.
        }
    }
}
