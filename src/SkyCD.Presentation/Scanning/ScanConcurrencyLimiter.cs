using System;
using System.Threading;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Blocks a worker until fewer than the controller's current target are active. The limit is polled so
/// a dynamically shrinking pool never oversubscribes.
/// </summary>
internal sealed class ScanConcurrencyLimiter
{
    private readonly object gate = new();
    private readonly Func<int> limitProvider;
    private int active;

    public ScanConcurrencyLimiter(Func<int> limitProvider)
    {
        this.limitProvider = limitProvider ?? throw new ArgumentNullException(nameof(limitProvider));
    }

    public void Acquire(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            while (active >= Math.Max(1, limitProvider()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(gate, 50);
            }

            active++;
        }
    }

    public void Release()
    {
        lock (gate)
        {
            if (active > 0)
            {
                active--;
            }

            Monitor.PulseAll(gate);
        }
    }

    public void NotifyLimitChanged()
    {
        lock (gate)
        {
            Monitor.PulseAll(gate);
        }
    }
}
