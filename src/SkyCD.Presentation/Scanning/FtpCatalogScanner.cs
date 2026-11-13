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

/// <summary>
/// Resilient FTP catalog traversal (#433).
/// Directory cycles are skipped, transient disconnects are retried with a bounded reconnect,
/// unreadable or vanished children fail as individual items and never abort the whole scan.
/// </summary>
public sealed class FtpCatalogScanner(IFtpCatalogConnector? connector = null)
{
    private readonly IFtpCatalogConnector connector = connector ?? new FtpCatalogConnector();

    public IReadOnlyList<CatalogDocument> Scan(
        FtpScanOptions options,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default) =>
        ScanDetailed(options, progress, cancellationToken).Documents;

    public CatalogScanResult ScanDetailed(
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

        // The FTP traversal shares the bounded retry, timeout and cancellation infrastructure
        // with the folder and media scans so a disconnect is survivable instead of fatal.
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

            using var scope = new FtpSessionScope(connector, location, username, password, context);
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
                throw new FtpConnectionException(
                    LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, $"{location.Host}:{location.Port}"));
            }

            var rootName = ResolveRootName(options, location);
            var rootId = $"ftp-{Guid.NewGuid():N}";
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
        catch (Exception ex) when (ex is FtpAuthenticationException or FtpConnectionException
                or FtpSchemeNotSupportedException or FtpDirectoryNotFoundException)
        {
            throw;
        }
        catch (ScanRetryExhaustedException ex)
        {
            var sanitized = SecretSanitizer.Sanitize(ex.Message);
            throw new FtpConnectionException(
                LocalizationService.Current.Format(
                    TextKeys.FtpConnectionFailed,
                    $"{location.Host}:{location.Port}") + $" ({sanitized})",
                ex);
        }
        catch (Exception ex)
        {
            throw new FtpConnectionException(
                LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, $"{location.Host}:{location.Port}"),
                ex);
        }
    }

    /// <summary>
    /// Lists a remote directory tree. Returns null when the directory was skipped (cycle,
    /// exclusion) or failed; in both cases the directory contributes no catalog document.
    /// </summary>
    private static RemoteDirectory? ListTree(
        ScanOperationContext context,
        FtpSessionScope scope,
        string path,
        string name,
        string? permission,
        DateTime? modifiedUtc,
        bool isRoot,
        HashSet<string> visited,
        ref int listedRef)
    {
        context.ThrowIfCancellationRequested();
        var normalized = FtpLocation.NormalizePath(path);

        if (!visited.Add(normalized))
        {
            // The directory points back to an already visited path: report the cycle instead of
            // recursing forever.
            ReportSkip(context, normalized, "FTP directory cycle detected.", "list-directory");
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

        IReadOnlyList<FtpRemoteEntry> entries;
        try
        {
            // Bounded retry with an automatic reconnect for transient FTP disconnects.
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
        catch (Exception ex) when (ex is FtpAuthenticationException or FtpSchemeNotSupportedException)
        {
            // Rejected credentials and unsupported protocols are fatal for the whole scan.
            throw;
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

    /// <summary>
    /// Owns the FTP control session and transparently re-establishes it after a dropped
    /// connection so a transient disconnect is survivable (#433).
    /// </summary>
    private sealed class FtpSessionScope : IDisposable
    {
        private readonly IFtpCatalogConnector connector;
        private readonly FtpLocation location;
        private readonly string username;
        private readonly string password;
        private readonly ScanOperationContext context;
        private IFtpRemoteSession? session;

        public FtpSessionScope(
            IFtpCatalogConnector connector,
            FtpLocation location,
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
        public IFtpRemoteSession Open()
        {
            Reset();
            session = connector.Connect(location, username, password, context.CancellationToken);
            return session;
        }

        /// <summary>
        /// Lists a remote directory. A dropped connection invalidates the session so the next
        /// attempt transparently reconnects instead of failing the whole scan.
        /// </summary>
        public IReadOnlyList<FtpRemoteEntry> ListDirectory(string path)
        {
            var active = session ?? Open();
            try
            {
                return active.ListDirectory(path);
            }
            catch (FtpConnectionException)
            {
                Reset();
                throw;
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                Reset();
                throw new FtpConnectionException(
                    LocalizationService.Current.Format(
                        TextKeys.FtpConnectionFailed,
                        $"{location.Host}:{location.Port}"),
                    ex);
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
            // A vanished directory or a rejected login is a protocol answer from a healthy
            // transport, not a broken connection: it must not trigger a reconnect.
            if (exception is FtpDirectoryNotFoundException or FtpAuthenticationException)
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

        public List<FtpRemoteEntry> Files { get; } = [];
    }
}
