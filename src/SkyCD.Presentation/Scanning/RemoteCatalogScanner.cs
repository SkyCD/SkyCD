using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Net.Sockets;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteCatalogScanner
{
    private readonly RemoteCatalogProviderRegistry providers;

    public RemoteCatalogScanner() : this(RemoteCatalogProviderRegistry.Default)
    {
    }

    public RemoteCatalogScanner(RemoteCatalogProviderRegistry providers) => this.providers = providers;

    public RemoteCatalogScanner(IRemoteCatalogConnector connector) : this(new RemoteCatalogProviderRegistry([connector]))
    {
    }

    public IReadOnlyList<CatalogDocument> Scan(
        RemoteScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanDetailed(options, progress, cancellationToken).Documents;

    public CatalogScanResult ScanDetailed(
        RemoteScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var location = RemoteLocation.Parse(options.Address, providers);
        var connector = providers.Get(location.Scheme);
        var (username, password) = connector.ResolveCredentials(new RemoteCredentials(options.Username, options.Password, options.Anonymous));
        using var context = new ScanOperationContext(
            options.Traversal,
            progress,
            cancellationToken);

        progress?.ReportStage(
            LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer),
            stagePercentage: null,
            overallPercentage: 5);
        progress?.ReportInfo(
            LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, location.PublicUrl),
            itemPath: location.PublicUrl);

        try
        {
            context.ThrowIfCancellationRequested();

            using var scope = new RemoteSessionScope(connector, location, username, password, context);
            ScanRetryExecutor.Execute(
                context,
                location.PublicUrl,
                "connect",
                scope.Open);

            var listedCount = 0;
            var tree = ListTree(
                context,
                scope,
                location.Path,
                name: location.Path,
                permission: null,
                modifiedUtc: null,
                isRoot: true,
                visited: new HashSet<string>(StringComparer.Ordinal),
                listedRef: ref listedCount);

            if (tree is null)
            {
                // The root is never skipped or failed silently: both cases are fatal.
                throw connector.ConnectionFailed(location);
            }

            var rootName = ResolveRootName(options, location);
            var rootId = $"{location.Scheme}-{Guid.NewGuid():N}";
            var documents = new List<CatalogDocument>();
            var totalSize = AddChildren(context, tree, rootId, options.IncludeExtendedInfo, documents);
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

            context.Statistics.IncrementDiscovered();

            progress?.ReportStage(
                LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer),
                stagePercentage: 100,
                overallPercentage: 70);
            progress?.ReportInfo(
                $"Scanning complete. Documents={documents.Count} ({totalSize} bytes). " +
                $"Skipped={context.Statistics.Skipped}, Failed={context.Statistics.Failed}.",
                itemPath: location.PublicUrl);

            return new CatalogScanResult
            {
                Documents = documents,
                Statistics = context.Statistics
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RemoteAuthenticationException or RemoteConnectionException
                or RemoteSchemeNotSupportedException or RemoteDirectoryNotFoundException
                or RemoteListingNotSupportedException)
        {
            throw;
        }
        catch (ScanRetryExhaustedException ex)
        {
            var sanitized = SecretSanitizer.Sanitize(ex.Message);
            throw connector.ConnectionFailed(location, ex, sanitized);
        }
        catch (Exception ex)
        {
            throw connector.ConnectionFailed(location, ex);
        }
    }

    /// <summary>
    /// Lists a remote directory tree. Returns null when the directory was skipped (cycle,
    /// exclusion) or failed; in both cases the directory contributes no catalog document.
    /// </summary>
    private static RemoteDirectory? ListTree(
        ScanOperationContext context,
        RemoteSessionScope scope,
        string path,
        string name,
        string? permission,
        DateTime? modifiedUtc,
        bool isRoot,
        HashSet<string> visited,
        ref int listedRef)
    {
        context.ThrowIfCancellationRequested();
        var normalized = scope.NormalizePath(path);

        if (!visited.Add(normalized))
        {
            // The directory points back to an already visited path: report the cycle instead of
            // recursing forever.
            ReportSkip(context, normalized, $"{scope.Scheme.ToUpperInvariant()} directory cycle detected.", "list-directory");
            return null;
        }

        listedRef++;
        var reading = LocalizationService.Current.Format(TextKeys.ReadingRemoteDirectory, normalized);
        context.Progress?.ReportStage(
            reading,
            stagePercentage: null,
            overallPercentage: Math.Min(65, 8 + (listedRef * 4)));
        context.Progress?.ReportItem(normalized);
        context.Progress?.ReportInfo(reading, itemPath: normalized);

        IReadOnlyList<RemoteEntry> entries;
        try
        {
            entries = ScanRetryExecutor.Execute(
                context,
                normalized,
                "list-directory",
                () => scope.ListDirectory(normalized));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RemoteAuthenticationException or RemoteSchemeNotSupportedException)
        {
            // Rejected credentials and unsupported protocols are fatal for the whole scan.
            throw;
        }
        catch (RemoteListingNotSupportedException) when (isRoot)
        {
            // The root is never skipped or failed silently: both cases are fatal.
            throw;
        }
        catch (RemoteListingNotSupportedException ex)
        {
            ReportFail(context, normalized, ex.Message, ex.Stage, ex);
            return null;
        }
        catch (ScanRetryExhaustedException ex)
        {
            if (isRoot)
            {
                throw;
            }

            ReportFail(context, normalized, ex.Message, ex.Stage, ex);
            return null;
        }
        catch (Exception ex) when (IsPerItemFailure(ex))
        {
            if (isRoot)
            {
                throw;
            }

            ReportFail(context, normalized, ex.Message, "list-directory", ex);
            return null;
        }

        context.Progress?.ReportInfo(
            LocalizationService.Current.Format(TextKeys.FoundItemsInRemoteDirectory, entries.Count, normalized),
            itemPath: normalized);

        var directory = new RemoteDirectory
        {
            Name = name,
            FullName = normalized,
            Permission = permission,
            ModifiedUtc = modifiedUtc
        };

        foreach (var entry in entries)
        {
            context.ThrowIfCancellationRequested();

            if (IsExcluded(context, entry.Name))
            {
                ReportSkip(context, entry.FullName, "Excluded by pattern.", "process-entry");
                continue;
            }

            if (!entry.IsDirectory)
            {
                directory.Files.Add(entry);
                context.Progress?.ReportItem(entry.FullName);
                continue;
            }

            var child = ListTree(
                context,
                scope,
                entry.FullName,
                entry.Name,
                entry.Permission,
                entry.ModifiedUtc,
                isRoot: false,
                visited,
                ref listedRef);

            if (child is not null)
            {
                directory.Directories.Add(child);
            }
        }

        return directory;
    }

    private static long AddChildren(
        ScanOperationContext context,
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
                properties["Size"] = file.Size.ToString(CultureInfo.InvariantCulture);
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
            context.Statistics.IncrementDiscovered();
            context.Progress?.ReportLog(
                OperationLogLevel.ItemProcessed,
                $"Added file: {file.Name}",
                itemPath: file.FullName,
                stage: "add-children");
        }

        foreach (var child in directory.Directories)
        {
            var childId = $"folder-{Guid.NewGuid():N}";
            var childSize = AddChildren(context, child, childId, includeExtendedInfo, documents);
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
            context.Statistics.IncrementDiscovered();
        }

        return total;
    }

    private static string ResolveRootName(RemoteScanOptions options, RemoteLocation location)
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

    private static bool IsExcluded(ScanOperationContext context, string name)
    {
        foreach (var pattern in context.Traversal.ExclusionPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (FileSystemName.MatchesSimpleExpression(pattern.Trim(), name, ignoreCase: false))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportSkip(ScanOperationContext context, string itemPath, string reason, string stage)
    {
        context.Statistics.IncrementSkipped();
        context.Progress?.ReportSkipped(itemPath, SecretSanitizer.Sanitize(reason), stage);
    }

    private static void ReportFail(
        ScanOperationContext context,
        string itemPath,
        string reason,
        string stage,
        Exception? exception = null)
    {
        context.Statistics.IncrementFailed();
        context.Progress?.ReportFailed(itemPath, SecretSanitizer.Sanitize(reason), stage, exception);
    }

    /// <summary>
    /// Failures that affect a single remote item and must not abort the whole scan.
    /// </summary>
    private static bool IsPerItemFailure(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or PathTooLongException
            or ArgumentException
            or NotSupportedException;

    private sealed class RemoteSessionScope : IDisposable
    {
        private readonly IRemoteCatalogConnector connector;
        private readonly RemoteLocation location;
        private readonly string username;
        private readonly string password;
        private readonly ScanOperationContext context;
        private IRemoteCatalogSession? session;

        public RemoteSessionScope(
            IRemoteCatalogConnector connector,
            RemoteLocation location,
            string username,
            string password,
            ScanOperationContext context)
        {
            this.connector = connector;
            this.location = location;
            this.username = username;
            this.password = password;
            this.context = context;
        }

        /// <summary>Opens a fresh control session, replacing any previous one.</summary>
        public IRemoteCatalogSession Open()
        {
            Reset();
            session = connector.Connect(location, username, password, context.CancellationToken);
            return session;
        }

        public string Scheme => location.Scheme;

        public string NormalizePath(string path) => connector.NormalizePath(path);

        public IReadOnlyList<RemoteEntry> ListDirectory(string path)
        {
            var active = session ?? Open();
            try
            {
                return active.ListDirectory(path);
            }
            catch (RemoteConnectionException)
            {
                Reset();
                throw;
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                Reset();
                throw connector.ConnectionFailed(location, ex);
            }
        }

        public void Dispose() => Reset();

        private void Reset()
        {
            var stale = session;
            session = null;
            stale?.Dispose();
        }

        private static bool IsConnectionFailure(Exception exception)
        {
            // A vanished directory, a rejected login or a refused listing is a protocol answer
            // from a healthy transport, not a broken connection: it must not trigger a reconnect.
            if (exception is RemoteDirectoryNotFoundException
                or RemoteAuthenticationException
                or RemoteListingNotSupportedException)
            {
                return false;
            }

            return exception is SocketException
                or IOException
                or ObjectDisposedException;
        }
    }

    private sealed class RemoteDirectory
    {
        public required string Name { get; init; }

        public required string FullName { get; init; }

        public string? Permission { get; init; }

        public DateTime? ModifiedUtc { get; init; }

        public List<RemoteDirectory> Directories { get; } = [];

        public List<RemoteEntry> Files { get; } = [];
    }
}
