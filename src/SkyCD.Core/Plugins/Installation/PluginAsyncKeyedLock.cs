using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Serializes operations per key. Keys are compared with <see cref="StringComparer.OrdinalIgnoreCase"/> and
/// released when no caller holds or waits for them.
/// </summary>
internal sealed class PluginAsyncKeyedLock
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Entry entry;
        lock (entries)
        {
            if (!entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                entries[key] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseEntry(key, entry, held: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    internal int TrackedKeyCount
    {
        get
        {
            lock (entries)
            {
                return entries.Count;
            }
        }
    }

    private void ReleaseEntry(string key, Entry entry, bool held)
    {
        if (held)
        {
            entry.Semaphore.Release();
        }

        lock (entries)
        {
            entry.References--;
            if (entry.References == 0)
            {
                entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class Releaser(PluginAsyncKeyedLock owner, string key, Entry entry) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.ReleaseEntry(key, entry, held: true);
            }
        }
    }
}
