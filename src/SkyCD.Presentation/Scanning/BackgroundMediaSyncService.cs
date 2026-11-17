using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Staging;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Serial, opt-in scans of already cataloged mounted media. Call Start on the
/// owning UI context so publication resumes there; traversal runs on a worker.
/// </summary>
public sealed class BackgroundMediaSyncService : IDisposable
{
    private readonly CatalogDocumentRepository repository;
    private readonly IMediaDriveProvider drives;
    private readonly Func<MediaScanOptions, CancellationToken, CatalogScanResult> scan;
    private readonly Action<IReadOnlyList<CatalogDocument>, CancellationToken> publish;
    private readonly Func<bool> canSync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private bool disposed;

    public BackgroundMediaSyncService(
        CatalogDocumentRepository repository,
        Action<IReadOnlyList<CatalogDocument>, CancellationToken> publish,
        Func<bool>? canSync = null,
        IMediaDriveProvider? drives = null,
        Func<MediaScanOptions, CancellationToken, CatalogScanResult>? scan = null)
    {
        this.repository = repository;
        this.publish = publish;
        this.canSync = canSync ?? (() => true);
        this.drives = drives ?? new SystemMediaDriveProvider();
        var scanner = new MediaCatalogScanner(this.drives);
        this.scan = scan ?? ((options, token) => scanner.ScanDetailed(options, cancellationToken: token));
    }

    public string? LastError { get; private set; }

    public event Action<string>? Failed;

    public void Start(TimeSpan? interval = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Stop();
        var cadence = interval ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThan(cadence, TimeSpan.FromSeconds(1));
        lifetime = new CancellationTokenSource();
        _ = RunAsync(cadence, lifetime.Token);
    }

    public void Stop()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    LastError = exception.Message;
                    Failed?.Invoke(exception.Message);
                }
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return;
        }
        try
        {
            if (!canSync()) return;
            LastError = null;
            var all = repository.GetAll();
            var siblingNames = all.ToLookup(entry => (entry.ParentId, entry.Name));
            var roots = all.Where(entry => entry.Type == CatalogDocumentType.Media
                && entry.Properties.ContainsKey("RootDirectory")
                && siblingNames[(entry.ParentId, entry.Name)].Count() == 1).ToArray();
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!canSync()) return;
                var path = Text(root, "RootDirectory");
                CatalogScanResult? result;
                try
                {
                    result = await Task.Run(() =>
                    {
                        var drive = drives.GetDrive(path);
                        if (drive is null || !Matches(root, drive)) return null;
                        var scanned = scan(new MediaScanOptions(path, root.Name, root.ParentId, root.Type,
                            Traversal: new ScanTraversalOptions
                            {
                                OperationTimeout = TimeSpan.FromMinutes(2),
                                LinkFollowPolicy = LinkFollowPolicy.NeverFollowLinks
                            }), cancellationToken);
                        var currentDrive = drives.GetDrive(path);
                        return currentDrive is not null && Matches(root, currentDrive) ? scanned : null;
                    }, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LastError = exception.Message;
                    Failed?.Invoke(exception.Message);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (disposed || !canSync()) return;
                var current = repository.Get(root.Id);
                // A catalog switch, deletion or rename during traversal invalidates the snapshot.
                if (result is null || result.Statistics.Failed > 0 || current is null || current.Name != root.Name
                    || current.ParentId != root.ParentId || Text(current, "RootDirectory") != path)
                {
                    continue;
                }
                var currentEntries = repository.GetAll();
                var byId = currentEntries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
                var merged = new CatalogEntryMerger().Merge(result.Documents, currentEntries);
                if (merged.Any(entry => !byId.TryGetValue(entry.Id, out var previous) || !Equivalent(previous, entry)))
                {
                    publish(result.Documents, cancellationToken);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool Matches(CatalogDocument root, MediaDriveInfo drive) =>
        drive.IsReady
        && string.Equals(Text(root, "VolumeLabel"), drive.VolumeLabel ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(Text(root, "DriveFormat"), drive.DriveFormat ?? string.Empty, StringComparison.OrdinalIgnoreCase)
        && Text(root, "TotalSize") == drive.TotalSize.ToString(CultureInfo.InvariantCulture);

    private static bool Equivalent(CatalogDocument previous, CatalogDocument current) =>
        previous.Name == current.Name && previous.ParentId == current.ParentId && previous.Type == current.Type
        && previous.Size == current.Size && previous.ChildrenCount == current.ChildrenCount
        && previous.Status == current.Status && previous.ContentsState == current.ContentsState
        && JsonSerializer.Serialize(previous.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            == JsonSerializer.Serialize(current.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    private static string Text(CatalogDocument document, string key) =>
        document.Properties.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }
}
