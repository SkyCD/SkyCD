using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

public sealed class FolderCatalogScanner
{
    public IReadOnlyList<CatalogDocument> Scan(FolderScanOptions options, IOperationProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.FolderPath) || !Directory.Exists(options.FolderPath))
        {
            throw new DirectoryToScanNotFoundException(options.FolderPath ?? string.Empty);
        }

        var rootDirectory = new DirectoryInfo(options.FolderPath);
        var rootName = !string.IsNullOrWhiteSpace(options.MediaName)
            ? options.MediaName.Trim()
            : rootDirectory.Name;

        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = rootDirectory.FullName;
        }

        progress?.ReportStage("Reading source folder...", stagePercentage: null, overallPercentage: 10);
        progress?.ReportInfo($"Starting folder scan: {options.FolderPath}", itemPath: options.FolderPath);

        var documents = new List<CatalogDocument>();
        var rootId = $"folder-{Guid.NewGuid():N}";

        var (totalSize, directChildrenCount, rootProperties) = ScanDirectory(
            rootDirectory,
            rootId,
            options.IncludeSubfolders,
            options.IncludeExtendedInfo,
            documents,
            progress);

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

        progress?.ReportStage("Scanning complete", stagePercentage: 100, overallPercentage: 100);
        progress?.ReportInfo($"Scanning complete. Scanned {documents.Count} documents ({totalSize} bytes).");
        return documents;
    }

    public IReadOnlyList<CatalogDocument> Scan(FolderScanOptions options, IProgress<(string Status, int Percentage)> progress) =>
        Scan(options, progress is not null ? new OperationProgressAdapter(progress) : null);

    private static (long TotalSize, int DirectChildrenCount, PropertiesCollection Properties) ScanDirectory(
        DirectoryInfo directory,
        string currentDocId,
        bool includeSubfolders,
        bool includeExtendedInfo,
        List<CatalogDocument> documents,
        IOperationProgress? progress)
    {
        long totalSize = 0;
        var directChildrenCount = 0;
        var dirProperties = new PropertiesCollection();

        if (includeExtendedInfo)
        {
            PopulateDirectoryProperties(dirProperties, directory);
        }

        FileInfo[] files;
        try
        {
            files = directory.GetFiles();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            progress?.ReportWarning($"Cannot read files in '{directory.FullName}': {ex.Message}", itemPath: directory.FullName);
            progress?.ReportSkipped(directory.FullName, ex.Message);
            files = [];
        }

        foreach (var file in files)
        {
            directChildrenCount++;
            totalSize += file.Length;

            var fileProperties = new PropertiesCollection();
            if (includeExtendedInfo)
            {
                PopulateFileProperties(fileProperties, file);
            }

            var fileDoc = new CatalogDocument
            {
                Id = $"file-{Guid.NewGuid():N}",
                Name = file.Name,
                ParentId = currentDocId,
                Type = CatalogDocumentType.File,
                Size = file.Length,
                ChildrenCount = 0,
                Properties = fileProperties
            };

            documents.Add(fileDoc);
            progress?.ReportItem(file.FullName);
            progress?.ReportLog(OperationLogLevel.ItemProcessed, $"Added file: {file.Name}", itemPath: file.FullName);
        }

        if (includeSubfolders)
        {
            DirectoryInfo[] subdirectories;
            try
            {
                subdirectories = directory.GetDirectories();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                progress?.ReportWarning($"Cannot read subdirectories in '{directory.FullName}': {ex.Message}", itemPath: directory.FullName);
                progress?.ReportSkipped(directory.FullName, ex.Message);
                subdirectories = [];
            }

            foreach (var subDir in subdirectories)
            {
                directChildrenCount++;
                var subDirId = $"folder-{Guid.NewGuid():N}";

                progress?.ReportStage($"Reading '{subDir.Name}'...", stagePercentage: null);
                progress?.ReportLog(OperationLogLevel.ItemProcessed, $"Scanning folder: {subDir.Name}", itemPath: subDir.FullName);

                var (subDirSize, subDirDirectChildren, subDirProperties) = ScanDirectory(
                    subDir,
                    subDirId,
                    includeSubfolders: true,
                    includeExtendedInfo,
                    documents,
                    progress);

                var subDirDoc = new CatalogDocument
                {
                    Id = subDirId,
                    Name = subDir.Name,
                    ParentId = currentDocId,
                    Type = CatalogDocumentType.Folder,
                    Size = subDirSize,
                    ChildrenCount = subDirDirectChildren,
                    Properties = subDirProperties
                };

                documents.Add(subDirDoc);
                totalSize += subDirSize;
            }
        }

        return (totalSize, directChildrenCount, dirProperties);
    }

    private static void PopulateFileProperties(PropertiesCollection properties, FileInfo file)
    {
        properties["Name"] = file.Name;
        properties["Size"] = file.Length.ToString(CultureInfo.InvariantCulture);
        properties["LastWriteTime"] = file.LastWriteTime.ToString("O");
        properties["LastAccessTime"] = file.LastAccessTime.ToString("O");
        properties["CreationTime"] = file.CreationTime.ToString("O");
        properties["IsReadOnly"] = file.IsReadOnly.ToString();
        properties["Attributes"] = file.Attributes.ToString();
        properties["FullName"] = file.FullName;
    }

    private static void PopulateDirectoryProperties(PropertiesCollection properties, DirectoryInfo dir)
    {
        properties["Name"] = dir.Name;
        properties["LastWriteTime"] = dir.LastWriteTime.ToString("O");
        properties["LastAccessTime"] = dir.LastAccessTime.ToString("O");
        properties["CreationTime"] = dir.CreationTime.ToString("O");
        properties["Attributes"] = dir.Attributes.ToString();
        properties["FullName"] = dir.FullName;
        properties["RealDirectoryFullName"] = dir.FullName;
        properties["RealDirectoryName"] = dir.Name;
    }
}
