using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Shared resilient directory traversal for folder and media catalog scans.
/// </summary>
internal sealed class FileSystemCatalogTraverser
{
    private readonly ScanOperationContext context;
    private readonly HashSet<string> visitedIdentities;
    private readonly object documentsGate = new();

    public FileSystemCatalogTraverser(ScanOperationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        visitedIdentities = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public (long TotalSize, int DirectChildrenCount, PropertiesCollection Properties) Traverse(
        string rootPath,
        string currentDocId,
        bool includeSubfolders,
        bool includeExtendedInfo,
        List<CatalogDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(documents);

        context.ThrowIfCancellationRequested();

        var rootEntry = ExecuteEnumeration(
            rootPath,
            stage: "resolve-root",
            fatalOnExhaustion: true,
            () => context.FileSystem.GetDirectoryEntry(rootPath));

        if (!visitedIdentities.Add(rootEntry.VisitedIdentity))
        {
            // Should not happen for a fresh traverser, but keep cycle-safe.
            ReportSkip(rootPath, "Root path already visited (cycle).", "resolve-root");
            return (0, 0, new PropertiesCollection());
        }

        return TraverseDirectory(
            rootEntry,
            currentDocId,
            includeSubfolders,
            includeExtendedInfo,
            documents,
            isRoot: true);
    }

    private (long TotalSize, int DirectChildrenCount, PropertiesCollection Properties) TraverseDirectory(
        ScanFileSystemEntry directory,
        string currentDocId,
        bool includeSubfolders,
        bool includeExtendedInfo,
        List<CatalogDocument> documents,
        bool isRoot)
    {
        context.ThrowIfCancellationRequested();

        long totalSize = 0;
        long directChildrenCount = 0;
        var dirProperties = new PropertiesCollection();

        if (includeExtendedInfo)
        {
            PopulateDirectoryProperties(dirProperties, directory);
        }

        var files = EnumerateEntries(
            directory.FullPath,
            stage: "enumerate-files",
            fatalOnExhaustion: isRoot,
            enumerate: () => context.FileSystem.EnumerateFiles(directory.FullPath));

        ProcessEntries(
            files,
            entry =>
            {
                if (!TryAcceptEntry(entry, out var skipReason))
                {
                    if (skipReason is not null)
                    {
                        ReportSkip(entry.FullPath, skipReason, "process-file");
                    }

                    return;
                }

                try
                {
                    context.ThrowIfCancellationRequested();
                    var fileProperties = new PropertiesCollection();
                    if (includeExtendedInfo)
                    {
                        PopulateFileProperties(fileProperties, entry);
                    }

                    var fileDoc = new CatalogDocument
                    {
                        Id = $"file-{Guid.NewGuid():N}",
                        Name = entry.Name,
                        ParentId = currentDocId,
                        Type = CatalogDocumentType.File,
                        Size = entry.Length,
                        ChildrenCount = 0,
                        Properties = fileProperties
                    };

                    lock (documentsGate)
                    {
                        documents.Add(fileDoc);
                    }

                    Interlocked.Add(ref totalSize, entry.Length);
                    Interlocked.Increment(ref directChildrenCount);
                    context.Statistics.IncrementDiscovered();
                    context.Progress?.ReportItem(entry.FullPath);
                    context.Progress?.ReportLog(
                        OperationLogLevel.ItemProcessed,
                        $"Added file: {entry.Name}",
                        itemPath: entry.FullPath,
                        stage: "process-file");
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsPerItemFailure(ex))
                {
                    ReportFail(entry.FullPath, ex.Message, "process-file", ex);
                }
            });

        if (!includeSubfolders)
        {
            return (totalSize, (int)directChildrenCount, dirProperties);
        }

        var subdirectories = EnumerateEntries(
            directory.FullPath,
            stage: "enumerate-directories",
            fatalOnExhaustion: isRoot,
            enumerate: () => context.FileSystem.EnumerateDirectories(directory.FullPath));

        foreach (var subDir in subdirectories)
        {
            context.ThrowIfCancellationRequested();

            if (!TryAcceptEntry(subDir, out var skipReason))
            {
                if (skipReason is not null)
                {
                    ReportSkip(subDir.FullPath, skipReason, "process-directory");
                }

                continue;
            }

            if (subDir.IsLinkOrReparsePoint)
            {
                if (context.Traversal.LinkFollowPolicy == LinkFollowPolicy.NeverFollowLinks)
                {
                    ReportSkip(subDir.FullPath, "Directory link not followed (NeverFollowLinks policy).", "process-directory");
                    continue;
                }

                if (!visitedIdentities.Add(subDir.VisitedIdentity))
                {
                    ReportSkip(subDir.FullPath, "Directory link cycle detected.", "process-directory");
                    continue;
                }
            }
            else if (!visitedIdentities.Add(subDir.VisitedIdentity))
            {
                ReportSkip(subDir.FullPath, "Directory cycle detected.", "process-directory");
                continue;
            }

            var subDirId = $"folder-{Guid.NewGuid():N}";
            context.Progress?.ReportStage($"Reading '{subDir.Name}'...", stagePercentage: null);
            context.Progress?.ReportLog(
                OperationLogLevel.ItemProcessed,
                $"Scanning folder: {subDir.Name}",
                itemPath: subDir.FullPath,
                stage: "process-directory");

            try
            {
                var (subDirSize, subDirDirectChildren, subDirProperties) = TraverseDirectory(
                    subDir,
                    subDirId,
                    includeSubfolders: true,
                    includeExtendedInfo,
                    documents,
                    isRoot: false);

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

                lock (documentsGate)
                {
                    documents.Add(subDirDoc);
                }

                Interlocked.Add(ref totalSize, subDirSize);
                Interlocked.Increment(ref directChildrenCount);
                context.Statistics.IncrementDiscovered();
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ScanRetryExhaustedException ex)
            {
                ReportFail(subDir.FullPath, ex.Message, ex.Stage, ex);
            }
            catch (Exception ex) when (IsPerItemFailure(ex))
            {
                ReportFail(subDir.FullPath, ex.Message, "process-directory", ex);
            }
        }

        return (totalSize, (int)directChildrenCount, dirProperties);
    }

    private void ProcessEntries(IReadOnlyList<ScanFileSystemEntry> entries, Action<ScanFileSystemEntry> process)
    {
        var concurrency = context.Traversal.GetBoundedMaxConcurrency();
        if (concurrency == 1 || entries.Count <= 1)
        {
            foreach (var entry in entries)
            {
                context.ThrowIfCancellationRequested();
                process(entry);
            }

            return;
        }

        try
        {
            Parallel.ForEach(
                entries,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = concurrency,
                    CancellationToken = context.CancellationToken
                },
                process);
        }
        catch (AggregateException ex)
        {
            ex.Flatten().Handle(inner =>
            {
                if (inner is OperationCanceledException)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    throw inner;
                }

                return false;
            });
        }
    }

    private IReadOnlyList<ScanFileSystemEntry> EnumerateEntries(
        string path,
        string stage,
        bool fatalOnExhaustion,
        Func<IEnumerable<ScanFileSystemEntry>> enumerate)
    {
        try
        {
            return ScanRetryExecutor.Execute(
                context,
                path,
                stage,
                () =>
                {
                    var list = new List<ScanFileSystemEntry>();
                    foreach (var entry in enumerate())
                    {
                        context.ThrowIfCancellationRequested();
                        list.Add(entry);
                    }

                    return (IReadOnlyList<ScanFileSystemEntry>)list;
                });
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScanRetryExhaustedException) when (fatalOnExhaustion)
        {
            throw;
        }
        catch (Exception ex) when (!fatalOnExhaustion && IsPerItemFailure(ex))
        {
            ReportFail(path, ex.Message, stage, ex);
            return [];
        }
        catch (ScanRetryExhaustedException ex) when (!fatalOnExhaustion)
        {
            ReportFail(path, ex.Message, stage, ex);
            return [];
        }
    }

    private T ExecuteEnumeration<T>(string path, string stage, bool fatalOnExhaustion, Func<T> action)
    {
        try
        {
            return ScanRetryExecutor.Execute(context, path, stage, action);
        }
        catch (ScanRetryExhaustedException) when (fatalOnExhaustion)
        {
            throw;
        }
    }

    private bool TryAcceptEntry(ScanFileSystemEntry entry, out string? skipReason)
    {
        skipReason = null;

        if (IsExcludedByPattern(entry.Name))
        {
            skipReason = "Excluded by pattern.";
            return false;
        }

        if (ShouldSkipHiddenOrSystem(entry.Attributes))
        {
            skipReason = "Hidden/system entry excluded by policy.";
            return false;
        }

        if (IsInvalidOrTooLongPath(entry.FullPath))
        {
            skipReason = "Path is invalid or exceeds supported length.";
            return false;
        }

        return true;
    }

    private bool IsExcludedByPattern(string name)
    {
        foreach (var pattern in context.Traversal.ExclusionPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (FileSystemName.MatchesSimpleExpression(pattern.Trim(), name, ignoreCase: OperatingSystem.IsWindows()))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldSkipHiddenOrSystem(FileAttributes attributes)
    {
        var policy = context.Traversal.HiddenSystemFilePolicy;
        var isHidden = (attributes & FileAttributes.Hidden) != 0;
        var isSystem = (attributes & FileAttributes.System) != 0;

        return policy switch
        {
            HiddenSystemFilePolicy.SkipHidden => isHidden,
            HiddenSystemFilePolicy.SkipSystem => isSystem,
            HiddenSystemFilePolicy.SkipHiddenAndSystem => isHidden || isSystem,
            _ => false
        };
    }

    private static bool IsInvalidOrTooLongPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            _ = Path.GetFullPath(path);
            return false;
        }
        catch (Exception ex) when (ex is PathTooLongException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    private void ReportSkip(string itemPath, string reason, string stage)
    {
        context.Statistics.IncrementSkipped();
        context.Progress?.ReportSkipped(itemPath, reason, stage);
    }

    private void ReportFail(string itemPath, string reason, string stage, Exception? exception = null)
    {
        context.Statistics.IncrementFailed();
        context.Progress?.ReportFailed(itemPath, reason, stage, exception);
    }

    private static bool IsPerItemFailure(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or PathTooLongException
            or ArgumentException
            or NotSupportedException
            or ScanRetryExhaustedException;

    private static void PopulateFileProperties(PropertiesCollection properties, ScanFileSystemEntry file)
    {
        properties["Name"] = file.Name;
        properties["Size"] = file.Length.ToString(CultureInfo.InvariantCulture);
        properties["LastWriteTime"] = file.LastWriteTime.ToString("O");
        properties["LastAccessTime"] = file.LastAccessTime.ToString("O");
        properties["CreationTime"] = file.CreationTime.ToString("O");
        properties["IsReadOnly"] = file.IsReadOnly.ToString();
        properties["Attributes"] = file.Attributes.ToString();
        properties["FullName"] = file.FullPath;
    }

    private static void PopulateDirectoryProperties(PropertiesCollection properties, ScanFileSystemEntry dir)
    {
        properties["Name"] = dir.Name;
        properties["LastWriteTime"] = dir.LastWriteTime.ToString("O");
        properties["LastAccessTime"] = dir.LastAccessTime.ToString("O");
        properties["CreationTime"] = dir.CreationTime.ToString("O");
        properties["Attributes"] = dir.Attributes.ToString();
        properties["FullName"] = dir.FullPath;
        properties["RealDirectoryFullName"] = dir.FullPath;
        properties["RealDirectoryName"] = dir.Name;
    }
}
