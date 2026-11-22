using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Shared resilient directory traversal for folder and media catalog scans. Subdirectories are
/// processed through a bounded producer/consumer queue so large trees parallelize across the whole
/// scan instead of only the file loop of one directory.
/// </summary>
internal sealed class FileSystemCatalogTraverser
{
    private readonly ScanOperationContext context;
    private readonly HashSet<string> visitedIdentities;
    private readonly object visitedGate = new();
    private readonly DocumentStore store = new();

    private BlockingCollection<DirectoryWorkItem>? queue;
    private ScanConcurrencySession? session;
    private DirectoryWorkItem? rootWorkItem;
    private bool includeFolders;
    private bool includeFiles;
    private bool includeExtendedInfo;

    public FileSystemCatalogTraverser(ScanOperationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        visitedIdentities = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public (long TotalSize, int DirectChildrenCount, PropertiesCollection Properties) Traverse(
        string rootPath,
        string currentDocId,
        bool includeFolders,
        bool includeFiles,
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

        if (!TryAddVisited(rootEntry.VisitedIdentity))
        {
            // Should not happen for a fresh traverser, but keep cycle-safe.
            ReportSkip(rootPath, "Root path already visited (cycle).", "resolve-root");
            return (0, 0, new PropertiesCollection());
        }

        this.includeFolders = includeFolders;
        this.includeFiles = includeFiles;
        this.includeExtendedInfo = includeExtendedInfo;

        var plan = ScanConcurrencyResolver.ResolvePlan(context.Traversal, Environment.ProcessorCount);
        using var concurrency = new ScanConcurrencySession(plan, context);
        session = concurrency;
        queue = new BlockingCollection<DirectoryWorkItem>();
        rootWorkItem = new DirectoryWorkItem(rootEntry, currentDocId, isRoot: true, parent: null);
        var workers = StartWorkers(plan.CeilingWorkers);

        try
        {
            queue.Add(rootWorkItem);
            WaitForRoot();
            foreach (var document in store.OrderedDocuments())
            {
                documents.Add(document);
            }

            return (rootWorkItem.TotalSize, rootWorkItem.DirectChildrenCount, rootWorkItem.Properties);
        }
        finally
        {
            queue.CompleteAdding();
            StopWorkers(workers);
            queue.Dispose();
            queue = null;
            session = null;
        }
    }

    private void WaitForRoot()
    {
        try
        {
            rootWorkItem!.Completion.Task.Wait(context.CancellationToken);
        }
        catch (AggregateException exception)
        {
            var inner = exception.Flatten().InnerExceptions.FirstOrDefault();
            if (inner is OperationCanceledException)
            {
                throw new OperationCanceledException(context.CancellationToken);
            }

            ExceptionDispatchInfo.Capture(inner ?? exception).Throw();
        }
    }

    private Thread[] StartWorkers(int workerCount)
    {
        var workers = new Thread[Math.Max(1, workerCount)];
        for (var index = 0; index < workers.Length; index++)
        {
            var worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "SkyCD scan worker"
            };
            TrySetBelowNormalPriority(worker);
            worker.Start();
            workers[index] = worker;
        }

        return workers;
    }

    private static void TrySetBelowNormalPriority(Thread worker)
    {
        try
        {
            worker.Priority = ThreadPriority.BelowNormal;
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or ThreadStateException)
        {
            // Not every platform lets a managed thread set its priority.
        }
    }

    private void StopWorkers(Thread[] workers)
    {
        foreach (var worker in workers)
        {
            try
            {
                worker.Join(TimeSpan.FromSeconds(2));
            }
            catch (ThreadStateException)
            {
                // The worker already exited.
            }
        }
    }

    private void WorkerLoop()
    {
        var concurrencyQueue = queue!;
        var concurrencySession = session!;
        try
        {
            while (!concurrencyQueue.IsCompleted)
            {
                context.ThrowIfCancellationRequested();
                if (!concurrencyQueue.TryTake(out var item, 100, context.CancellationToken))
                {
                    continue;
                }

                context.ThrowIfCancellationRequested();
                concurrencySession.Limiter.Acquire(context.CancellationToken);
                try
                {
                    ProcessDirectory(item!, concurrencyQueue);
                }
                finally
                {
                    concurrencySession.Limiter.Release();
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            FailRoot(new OperationCanceledException(context.CancellationToken));
        }
        catch (InvalidOperationException) when (concurrencyQueue.IsCompleted)
        {
            // Completed while waiting for the queue.
        }
        catch (Exception exception)
        {
            FailRoot(exception);
        }
    }

    private void ProcessDirectory(DirectoryWorkItem item, BlockingCollection<DirectoryWorkItem> concurrencyQueue)
    {
        var completed = false;
        try
        {
            if (includeExtendedInfo)
            {
                PopulateDirectoryProperties(item.Properties, item.Entry);
            }

            if (includeFiles)
            {
                var (fileSize, fileCount) = TraverseFiles(item);
                item.TotalSize += fileSize;
                item.DirectChildrenCount += fileCount;
            }

            if (includeFolders)
            {
                EnqueueSubdirectories(item, concurrencyQueue);
            }

            completed = true;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!item.IsRoot && IsPerItemFailure(exception))
        {
            ReportFail(item.Entry.FullPath, exception.Message, "process-directory", exception);
            completed = true;
        }
        finally
        {
            // A fatal failure must reach the root task; finalizing here would complete it successfully first.
            if (completed)
            {
                lock (item.Gate)
                {
                    item.OwnWorkCompleted = true;
                }

                TryFinalize(item);
            }
        }
    }

    private (long TotalSize, int FileCount) TraverseFiles(DirectoryWorkItem item)
    {
        long totalSize = 0;
        int fileCount = 0;

        var files = EnumerateEntries(
            item.Entry.FullPath,
            stage: "enumerate-files",
            fatalOnExhaustion: item.IsRoot,
            enumerate: () => context.FileSystem.EnumerateFiles(item.Entry.FullPath));

        foreach (var entry in files)
        {
            context.ThrowIfCancellationRequested();

            if (!TryAcceptEntry(entry, out var skipReason))
            {
                if (skipReason is not null)
                {
                    ReportSkip(entry.FullPath, skipReason, "process-file");
                }

                continue;
            }

            try
            {
                context.ThrowIfCancellationRequested();
                var fileProperties = new PropertiesCollection
                {
                    ["FullName"] = entry.FullPath,
                    ["LastWriteTime"] = entry.LastWriteTime.ToString("O")
                };
                if (includeExtendedInfo)
                {
                    PopulateFileProperties(fileProperties, entry);
                }

                var fileDoc = new CatalogDocument
                {
                    Id = $"file-{Guid.NewGuid():N}",
                    Name = entry.Name,
                    ParentId = item.DocId,
                    Type = CatalogDocumentType.File,
                    Size = entry.Length,
                    ChildrenCount = 0,
                    Properties = fileProperties
                };

                store.Add(entry.FullPath, fileDoc);
                totalSize += entry.Length;
                fileCount++;
                context.Statistics.IncrementDiscovered();
                session?.Controller.RecordCompleted();
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
            catch (Exception exception) when (IsPerItemFailure(exception))
            {
                ReportFail(entry.FullPath, exception.Message, "process-file", exception);
            }
        }

        return (totalSize, fileCount);
    }

    private void EnqueueSubdirectories(DirectoryWorkItem item, BlockingCollection<DirectoryWorkItem> concurrencyQueue)
    {
        var subdirectories = EnumerateEntries(
            item.Entry.FullPath,
            stage: "enumerate-directories",
            fatalOnExhaustion: item.IsRoot,
            enumerate: () => context.FileSystem.EnumerateDirectories(item.Entry.FullPath));

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

            if (!TryEnterDirectory(subDir, out skipReason))
            {
                if (skipReason is not null)
                {
                    ReportSkip(subDir.FullPath, skipReason, "process-directory");
                }

                continue;
            }

            var subDirId = $"folder-{Guid.NewGuid():N}";
            context.Progress?.ReportStage($"Reading '{subDir.Name}'...", stagePercentage: null);
            context.Progress?.ReportLog(
                OperationLogLevel.ItemProcessed,
                $"Scanning folder: {subDir.Name}",
                itemPath: subDir.FullPath,
                stage: "process-directory");

            var child = new DirectoryWorkItem(
                subDir,
                subDirId,
                isRoot: false,
                parent: item);
            try
            {
                lock (item.Gate)
                {
                    item.PendingChildren++;
                }

                concurrencyQueue.Add(child, context.CancellationToken);
            }
            catch
            {
                lock (item.Gate)
                {
                    item.PendingChildren--;
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Finalizes an item once all of its children finished, creating its folder document and folding
    /// its totals into the parent. Runs without holding the item gate except for the state snapshots.
    /// </summary>
    private void TryFinalize(DirectoryWorkItem item)
    {
        lock (item.Gate)
        {
            if (!item.OwnWorkCompleted || item.PendingChildren > 0 || item.Finalized)
            {
                return;
            }

            item.Finalized = true;
        }

        var skip = !item.IsRoot && context.Traversal.SkipEmptyFolders && item.DirectChildrenCount == 0;
        if (skip)
        {
            ReportSkip(item.Entry.FullPath, "Empty folder excluded by policy.", "process-directory");
        }
        else if (!item.IsRoot)
        {
            var subDirDoc = new CatalogDocument
            {
                Id = item.DocId,
                Name = item.Entry.Name,
                ParentId = item.Parent!.DocId,
                Type = CatalogDocumentType.Folder,
                Size = item.TotalSize,
                ChildrenCount = item.DirectChildrenCount,
                Properties = item.Properties
            };

            store.Add(item.Entry.FullPath, subDirDoc);
            context.Statistics.IncrementDiscovered();
            session?.Controller.RecordCompleted();
        }

        var parent = item.Parent;
        if (parent is null)
        {
            item.Completion.TrySetResult(true);
            TryCompleteQueue();
            return;
        }

        bool parentReady;
        lock (parent.Gate)
        {
            if (!skip)
            {
                parent.TotalSize += item.TotalSize;
                parent.DirectChildrenCount++;
            }

            parent.PendingChildren--;
            parentReady = parent.OwnWorkCompleted && parent.PendingChildren == 0;
        }

        if (parentReady)
        {
            TryFinalize(parent);
        }
    }

    private void TryCompleteQueue()
    {
        try
        {
            queue?.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // The scan is already tearing down.
        }
    }

    private void FailRoot(Exception exception)
    {
        var root = rootWorkItem;
        if (root is null)
        {
            return;
        }

        root.Completion.TrySetException(exception);
        TryCompleteQueue();
    }

    private bool TryEnterDirectory(ScanFileSystemEntry subDir, out string? skipReason)
    {
        skipReason = null;

        if (subDir.IsLinkOrReparsePoint)
        {
            if (context.Traversal.LinkFollowPolicy == LinkFollowPolicy.NeverFollowLinks)
            {
                skipReason = "Directory link not followed (NeverFollowLinks policy).";
                return false;
            }

            if (!TryAddVisited(subDir.VisitedIdentity))
            {
                skipReason = "Directory link cycle detected.";
                return false;
            }

            return true;
        }

        if (!TryAddVisited(subDir.VisitedIdentity))
        {
            skipReason = "Directory cycle detected.";
            return false;
        }

        return true;
    }

    private bool TryAddVisited(string identity)
    {
        lock (visitedGate)
        {
            return visitedIdentities.Add(identity);
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

        skipReason = CheckWellKnownExclusion(entry);
        if (skipReason is not null)
        {
            return false;
        }

        if (ShouldSkipHiddenOrSystem(entry.Attributes))
        {
            skipReason = "Hidden/system entry excluded by policy.";
            return false;
        }

        if (context.Traversal.SkipOnlineOnlyCloudFiles && (entry.Attributes & FileAttributes.Offline) != 0)
        {
            skipReason = "Online-only cloud file excluded by policy.";
            return false;
        }

        skipReason = CheckScanRules(entry);
        if (skipReason is not null)
        {
            return false;
        }

        if (IsInvalidOrTooLongPath(entry.FullPath))
        {
            skipReason = "Path is invalid or exceeds supported length.";
            return false;
        }

        return true;
    }

    private string? CheckWellKnownExclusion(ScanFileSystemEntry entry)
    {
        if (context.Traversal.SkipRecycleBins && WellKnownScanExclusions.RecycleBinNames.Contains(entry.Name))
        {
            return "Recycle bin excluded by policy.";
        }

        if (context.Traversal.SkipWellKnownSystemFolders)
        {
            var wellKnownNames = entry.IsDirectory
                ? WellKnownScanExclusions.SystemFolderNames
                : WellKnownScanExclusions.SystemFileNames;
            if (wellKnownNames.Contains(entry.Name))
            {
                return "Well-known system entry excluded by policy.";
            }
        }

        return null;
    }

    private string? CheckScanRules(ScanFileSystemEntry entry)
    {
        var ignoreCase = OperatingSystem.IsWindows();
        if (ScanExclusionRule.IsExcludedByAny(context.Traversal.ExclusionRules, entry.Name, entry.IsDirectory, ignoreCase))
        {
            return "Excluded by rule.";
        }

        if (!ScanExclusionRule.PassesInclusion(context.Traversal.InclusionRules, entry.Name, entry.IsDirectory, ignoreCase))
        {
            return "Not matched by any inclusion rule.";
        }

        return null;
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
        session?.Controller.RecordBackoff();
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

    private sealed class DirectoryWorkItem
    {
        public DirectoryWorkItem(
            ScanFileSystemEntry entry,
            string docId,
            bool isRoot,
            DirectoryWorkItem? parent)
        {
            Entry = entry;
            DocId = docId;
            IsRoot = isRoot;
            Parent = parent;
        }

        public ScanFileSystemEntry Entry { get; }

        public string DocId { get; }

        public bool IsRoot { get; }

        public DirectoryWorkItem? Parent { get; }

        public object Gate { get; } = new();

        public PropertiesCollection Properties { get; } = new();

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PendingChildren;
        public bool OwnWorkCompleted;
        public bool Finalized;
        public long TotalSize;
        public int DirectChildrenCount;
    }

    private sealed class DocumentStore
    {
        private readonly object gate = new();
        private readonly List<(string SortKey, CatalogDocument Document)> entries = [];

        public void Add(string sortKey, CatalogDocument document)
        {
            lock (gate)
            {
                entries.Add((sortKey, document));
            }
        }

        /// <summary>
        /// Path-ordered so parallel scans emit the same sequence on every run regardless of which
        /// directory a worker happened to pick up first.
        /// </summary>
        public IReadOnlyList<CatalogDocument> OrderedDocuments()
        {
            lock (gate)
            {
                return entries
                    .OrderBy(entry => entry.SortKey, StringComparer.Ordinal)
                    .Select(entry => entry.Document)
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Owns the adaptive controller and the load-sampling timer for one traversal. The timer only runs
    /// in automatic mode; manual overrides stay fixed.
    /// </summary>
    private sealed class ScanConcurrencySession : IDisposable
    {
        private readonly Timer? loadTimer;

        public ScanConcurrencySession(ScanConcurrencyPlan plan, ScanOperationContext context)
        {
            Controller = new ScanConcurrencyController(plan, context.Traversal);
            Limiter = new ScanConcurrencyLimiter(() => Controller.Current);

            var interval = context.Traversal.LoadSampleInterval;
            if (context.Traversal.ConcurrencyMode == ScanConcurrencyMode.Automatic &&
                interval > TimeSpan.Zero && plan.CeilingWorkers > 1)
            {
                loadTimer = new Timer(
                    _ => SampleLoad(context),
                    state: null,
                    dueTime: interval,
                    period: interval);
            }
        }

        public ScanConcurrencyController Controller { get; }

        public ScanConcurrencyLimiter Limiter { get; }

        private void SampleLoad(ScanOperationContext context)
        {
            try
            {
                if (Controller.SampleLoad(context.CpuLoadSampler.Sample()))
                {
                    Limiter.NotifyLimitChanged();
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Load sampling must never break the scan.
            }
        }

        public void Dispose() => loadTimer?.Dispose();
    }
}
