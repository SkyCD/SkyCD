using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Sources;

/// <summary>
/// Spaces calls to a source so that consecutive requests are at least a fixed interval apart.
/// MusicBrainz documents one request per second for anonymous clients; concurrent callers are serialized.
/// </summary>
public sealed class MinimumIntervalThrottle : IRequestThrottle, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;
    private DateTimeOffset lastRequest = DateTimeOffset.MinValue;

    public MinimumIntervalThrottle(TimeSpan interval, TimeProvider? timeProvider = null)
    {
        this.interval = interval;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            var elapsed = now - lastRequest;
            if (lastRequest != DateTimeOffset.MinValue && elapsed < interval)
            {
                var delay = interval - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }

            lastRequest = timeProvider.GetUtcNow();
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
