using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class FtpCatalogScanner(IFtpCatalogConnector? connector = null)
{
    private readonly IFtpCatalogConnector connector = connector ?? new FtpCatalogConnector();

    public IReadOnlyList<CatalogDocument> Scan(
        FtpScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var location = FtpLocation.Parse(options.Address);
        var (username, password) = FtpCredentials.Resolve(options.Username, options.Password, options.Anonymous);
        if (!options.Anonymous &&
            !FtpCredentials.IsAnonymousUserName(username) &&
            string.IsNullOrEmpty(password))
        {
            throw new FtpAuthenticationException(LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed));
        }

        progress?.ReportStage(
            LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer),
            stagePercentage: null,
            overallPercentage: 5);
        progress?.ReportInfo(
            LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, location.PublicUrl),
            itemPath: location.PublicUrl);

        try
        {
            using var session = connector.Connect(location, username, password, cancellationToken);
            var state = new ScanState();
            var tree = ListTree(
                session,
                location.Path,
                name: location.Path,
                permission: null,
                modifiedUtc: null,
                progress,
                cancellationToken,
                state);

            var rootName = ResolveRootName(options, location);
            var rootId = $"ftp-{Guid.NewGuid():N}";
            var documents = new List<CatalogDocument>();
            var totalSize = AddChildren(tree, rootId, options.IncludeExtendedInfo, documents);
            var rootProperties = new PropertiesCollection();
            if (options.IncludeExtendedInfo)
            {
                rootProperties["URL"] = location.PublicUrl;
                rootProperties["FullName"] = location.Path;
                rootProperties["RealPath"] = location.Path;
            }

            documents.Insert(0, new CatalogDocument
            {
                Id = rootId,
                Name = rootName,
                ParentId = options.TargetParentId,
                Type = CatalogDocumentType.NetworkFolder,
                Size = totalSize,
                ChildrenCount = tree.Files.Count + tree.Directories.Count,
                Properties = rootProperties
            });

            progress?.ReportStage(
                LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer),
                stagePercentage: 100,
                overallPercentage: 70);
            progress?.ReportInfo(
                $"Scanning complete. Documents={documents.Count} ({totalSize} bytes).",
                itemPath: location.PublicUrl);
            return documents;
        }
        catch (Exception ex) when (ex is FtpAuthenticationException or FtpConnectionException or FtpSchemeNotSupportedException or FtpDirectoryNotFoundException or OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FtpConnectionException(
                LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, $"{location.Host}:{location.Port}"),
                ex);
        }
    }

    private static RemoteDirectory ListTree(
        IFtpRemoteSession session,
        string path,
        string name,
        string? permission,
        DateTime? modifiedUtc,
        IOperationProgress? progress,
        CancellationToken cancellationToken,
        ScanState state)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = FtpLocation.NormalizePath(path);
        var directory = new RemoteDirectory
        {
            Name = name,
            FullName = normalized,
            Permission = permission,
            ModifiedUtc = modifiedUtc
        };

        if (!state.Visited.Add(normalized))
        {
            return directory;
        }

        state.Listed++;
        progress?.ReportStage(
            LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, normalized),
            stagePercentage: null,
            overallPercentage: Math.Min(65, 8 + (state.Listed * 4)));
        progress?.ReportItem(normalized);

        foreach (var entry in session.ListDirectory(normalized))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                directory.Directories.Add(ListTree(
                    session,
                    entry.FullName,
                    entry.Name,
                    entry.Permission,
                    entry.ModifiedUtc,
                    progress,
                    cancellationToken,
                    state));
                continue;
            }

            directory.Files.Add(entry);
            progress?.ReportItem(entry.FullName);
        }

        return directory;
    }

    private static long AddChildren(
        RemoteDirectory directory,
        string parentId,
        bool includeExtendedInfo,
        List<CatalogDocument> documents)
    {
        long total = 0;
        foreach (var file in directory.Files)
        {
            var properties = new PropertiesCollection();
            if (includeExtendedInfo)
            {
                properties["RealPath"] = file.DirectoryPath;
                properties["FullName"] = file.FullName;
                properties["Size"] = file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(file.Permission))
                {
                    properties["Permission"] = file.Permission;
                }

                if (file.ModifiedUtc.HasValue)
                {
                    properties["FileDateTime"] = file.ModifiedUtc.Value.ToString("O");
                }
            }

            documents.Add(new CatalogDocument
            {
                Id = $"file-{Guid.NewGuid():N}",
                Name = file.Name,
                ParentId = parentId,
                Type = CatalogDocumentType.File,
                Size = file.Size,
                ChildrenCount = 0,
                Properties = properties
            });
            total += file.Size;
        }

        foreach (var child in directory.Directories)
        {
            var childId = $"folder-{Guid.NewGuid():N}";
            var childSize = AddChildren(child, childId, includeExtendedInfo, documents);
            var properties = new PropertiesCollection();
            if (includeExtendedInfo)
            {
                properties["RealPath"] = child.FullName;
                properties["FullName"] = child.FullName;
                if (!string.IsNullOrWhiteSpace(child.Permission))
                {
                    properties["Permission"] = child.Permission;
                }

                if (child.ModifiedUtc.HasValue)
                {
                    properties["FileDateTime"] = child.ModifiedUtc.Value.ToString("O");
                }
            }

            documents.Add(new CatalogDocument
            {
                Id = childId,
                Name = child.Name,
                ParentId = parentId,
                Type = CatalogDocumentType.Folder,
                Size = childSize,
                ChildrenCount = child.Files.Count + child.Directories.Count,
                Properties = properties
            });
            total += childSize;
        }

        return total;
    }

    private static string ResolveRootName(FtpScanOptions options, FtpLocation location)
    {
        if (!string.IsNullOrWhiteSpace(options.MediaName))
        {
            return options.MediaName.Trim();
        }

        var trimmed = location.Path.Trim('/');
        if (trimmed.Length == 0)
        {
            return location.Host;
        }

        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private sealed class ScanState
    {
        public HashSet<string> Visited { get; } = new(StringComparer.Ordinal);

        public int Listed { get; set; }
    }

    private sealed class RemoteDirectory
    {
        public required string Name { get; init; }

        public required string FullName { get; init; }

        public string? Permission { get; init; }

        public DateTime? ModifiedUtc { get; init; }

        public List<RemoteDirectory> Directories { get; } = [];

        public List<FtpRemoteEntry> Files { get; } = [];
    }
}
