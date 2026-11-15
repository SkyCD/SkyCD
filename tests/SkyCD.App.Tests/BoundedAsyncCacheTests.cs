using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.MediaCache;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the bounded asynchronous cache that backs preview and thumbnail decoding: one load per key,
/// least-recently-used eviction inside hard entry and byte budgets, and cancellation that only affects the
/// caller that asked for it.
/// </summary>
public sealed class BoundedAsyncCacheTests
{
    [Fact]
    public async Task ConcurrentRequestsForOneKey_ShareASingleLoad()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);
        var loads = 0;

        var requests = new List<Task<Payload?>>();
        for (var i = 0; i < 16; i++)
        {
            requests.Add(cache.GetAsync("key", _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<Payload?>(new Payload(10));
            }));
        }

        var results = await Task.WhenAll(requests);

        Assert.Equal(1, loads);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task EntryBudget_EvictsTheLeastRecentlyUsedValue()
    {
        using var cache = new BoundedAsyncCache<Payload>(2, 1024, payload => payload.Bytes);

        await cache.GetAsync("a", _ => Task.FromResult<Payload?>(new Payload(1)));
        await cache.GetAsync("b", _ => Task.FromResult<Payload?>(new Payload(1)));

        // Touch "a" so "b" becomes the least recently used entry.
        Assert.True(cache.TryGet("a", out _));

        await cache.GetAsync("c", _ => Task.FromResult<Payload?>(new Payload(1)));

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public async Task ByteBudget_EvictsUntilTheBudgetIsMet()
    {
        using var cache = new BoundedAsyncCache<Payload>(64, 30, payload => payload.Bytes);

        await cache.GetAsync("a", _ => Task.FromResult<Payload?>(new Payload(10)));
        await cache.GetAsync("b", _ => Task.FromResult<Payload?>(new Payload(10)));
        await cache.GetAsync("c", _ => Task.FromResult<Payload?>(new Payload(10)));
        await cache.GetAsync("d", _ => Task.FromResult<Payload?>(new Payload(10)));

        Assert.Equal(3, cache.Count);
        Assert.Equal(30, cache.TotalBytes);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public async Task ValueAboveTheWholeBudget_IsServedButNotRetained()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 10, payload => payload.Bytes);

        var served = await cache.GetAsync("huge", _ => Task.FromResult<Payload?>(new Payload(999)));

        Assert.NotNull(served);
        Assert.Equal(999, served!.Bytes);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public async Task NullResults_AreNotCached()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);
        var loads = 0;

        for (var i = 0; i < 2; i++)
        {
            var result = await cache.GetAsync("missing", _ =>
            {
                loads++;
                return Task.FromResult<Payload?>(null);
            });
            Assert.Null(result);
        }

        Assert.Equal(2, loads);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task CancelledCaller_StopsWaitingWithoutAbortingTheSharedLoad()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new CancellationTokenSource();

        var first = cache.GetAsync("key", async _ =>
        {
            await release.Task;
            return new Payload(5);
        }, cancelled.Token);

        var second = cache.GetAsync("key", _ => Task.FromResult<Payload?>(new Payload(5)));

        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        release.SetResult();
        var value = await second;

        Assert.NotNull(value);
        Assert.Equal(5, value!.Bytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task FailingLoad_IsNotCachedAndAllowsARetry()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("key", _ =>
        {
            attempts++;
            throw new InvalidOperationException("decode failed");
        }));

        var value = await cache.GetAsync("key", _ =>
        {
            attempts++;
            return Task.FromResult<Payload?>(new Payload(3));
        });

        Assert.Equal(2, attempts);
        Assert.NotNull(value);
        Assert.Equal(0, cache.PendingCount);
    }

    [Fact]
    public async Task Clear_ReleasesEveryCachedValue()
    {
        using var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);

        await cache.GetAsync("a", _ => Task.FromResult<Payload?>(new Payload(10)));
        await cache.GetAsync("b", _ => Task.FromResult<Payload?>(new Payload(10)));

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public async Task DisposedCache_RejectsNewRequests()
    {
        var cache = new BoundedAsyncCache<Payload>(8, 1024, payload => payload.Bytes);
        await cache.GetAsync("a", _ => Task.FromResult<Payload?>(new Payload(1)));

        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            cache.GetAsync("b", _ => Task.FromResult<Payload?>(new Payload(1))));
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(8, 0)]
    public void NonPositiveBudgets_AreRejected(int maxEntries, long maxBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundedAsyncCache<Payload>(maxEntries, maxBytes, payload => payload.Bytes));
    }

    private sealed record Payload(long Bytes);
}
