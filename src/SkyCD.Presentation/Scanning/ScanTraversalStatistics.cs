using System.Threading;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Accurate discovered / skipped / failed counters for a catalog scan traversal.
/// </summary>
public sealed class ScanTraversalStatistics
{
    private long discovered;
    private long skipped;
    private long failed;

    public long Discovered => Interlocked.Read(ref discovered);
    public long Skipped => Interlocked.Read(ref skipped);
    public long Failed => Interlocked.Read(ref failed);

    internal void IncrementDiscovered() => Interlocked.Increment(ref discovered);
    internal void IncrementSkipped() => Interlocked.Increment(ref skipped);
    internal void IncrementFailed() => Interlocked.Increment(ref failed);
}
