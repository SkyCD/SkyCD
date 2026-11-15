using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Asynchronous cache with a hard entry and byte budget, least-recently-used eviction and request
/// coalescing. Concurrent requests for one key share a single load, so scrolling or selecting quickly
/// through a catalog never starts one decode per item, and the working set stays bounded no matter how
/// many entries the browser walks past.
/// </summary>
/// <remarks>
/// A caller's cancellation stops that caller from waiting; it does not abort a load other callers are still
/// waiting on. The load itself receives the cache lifetime token and is aborted only on disposal.
/// </remarks>
public sealed class BoundedAsyncCache<TValue> : IDisposable where TValue : class
{
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> index = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> recency = new();
    private readonly Dictionary<string, Task<TValue?>> pending = new(StringComparer.Ordinal);
    private readonly Func<TValue, long> measure;
    private readonly int maxEntries;
    private readonly long maxBytes;
    private readonly CancellationTokenSource lifetime = new();
    private long totalBytes;
    private bool isDisposed;

    public BoundedAsyncCache(int maxEntries, long maxBytes, Func<TValue, long> measure)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "The entry budget must be positive.");
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "The byte budget must be positive.");
        }

        this.measure = measure ?? throw new ArgumentNullException(nameof(measure));
        this.maxEntries = maxEntries;
        this.maxBytes = maxBytes;
    }

    /// <summary>Cached values currently held.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return recency.Count;
            }
        }
    }

    /// <summary>Measured size of the cached values currently held.</summary>
    public long TotalBytes
    {
        get
        {
            lock (gate)
            {
                return totalBytes;
            }
        }
    }

    /// <summary>Loads in flight, including the ones this caller is coalesced onto.</summary>
    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count;
            }
        }
    }

    /// <summary>Returns a cached value, or loads it once and caches it. Null results are never cached.</summary>
    public async Task<TValue?> GetAsync(
        string key,
        Func<CancellationToken, Task<TValue?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        TaskCompletionSource<TValue?>? owner = null;
        Task<TValue?> request;

        lock (gate)
        {
            if (index.TryGetValue(key, out var node))
            {
                Promote(node);
                return node.Value.Value;
            }

            if (!pending.TryGetValue(key, out request!))
            {
                owner = new TaskCompletionSource<TValue?>(TaskCreationOptions.RunContinuationsAsynchronously);
                request = owner.Task;
                pending[key] = request;
            }
        }

        if (owner is not null)
        {
            // Started outside the lock and never awaited inline, so a caller that gives up stops waiting
            // without holding back the load itself. Every failure is funnelled into the completion source.
            _ = RunLoadAsync(key, factory, owner);
        }

        return await request.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryGet(string key, out TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (gate)
        {
            if (index.TryGetValue(key, out var node))
            {
                Promote(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Drops every cached value. Loads already in flight are not aborted.</summary>
    public void Clear()
    {
        lock (gate)
        {
            index.Clear();
            recency.Clear();
            totalBytes = 0;
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        Clear();
    }

    private async Task RunLoadAsync(
        string key,
        Func<CancellationToken, Task<TValue?>> factory,
        TaskCompletionSource<TValue?> owner)
    {
        TValue? value = default;
        Exception? failure = null;

        try
        {
            value = await factory(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (gate)
        {
            pending.Remove(key);
        }

        if (failure is not null)
        {
            owner.TrySetException(failure);
            return;
        }

        if (value is not null)
        {
            Store(key, value);
        }

        owner.TrySetResult(value);
    }

    private void Store(string key, TValue value)
    {
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            var size = Math.Max(0, measure(value));

            // A single value above the whole budget is served but never retained.
            if (size > maxBytes)
            {
                return;
            }

            if (index.TryGetValue(key, out var existing))
            {
                totalBytes -= existing.Value.Bytes;
                recency.Remove(existing);
                index.Remove(key);
            }

            var node = recency.AddFirst(new CacheEntry(key, value, size));
            index[key] = node;
            totalBytes += size;

            while (recency.Count > 0 && (recency.Count > maxEntries || totalBytes > maxBytes))
            {
                var oldest = recency.Last!;
                recency.RemoveLast();
                index.Remove(oldest.Value.Key);
                totalBytes -= oldest.Value.Bytes;
            }
        }
    }

    private void Promote(LinkedListNode<CacheEntry> node)
    {
        if (node.List is null || ReferenceEquals(recency.First, node))
        {
            return;
        }

        recency.Remove(node);
        recency.AddFirst(node);
    }

    private readonly record struct CacheEntry(string Key, TValue Value, long Bytes);
}
