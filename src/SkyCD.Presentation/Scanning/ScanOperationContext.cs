using System;
using System.Threading;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Shared scan-operation context: cancellation, progress, traversal policy and statistics.
/// Provides the cancellation lifecycle surface needed by resilient traversal (#433) and
/// cooperative cancellation (#432).
/// </summary>
public sealed class ScanOperationContext : IDisposable
{
    private readonly CancellationTokenSource? timeoutCts;
    private readonly CancellationTokenSource linkedCts;
    private bool disposed;

    public ScanOperationContext(
        ScanTraversalOptions? traversal = null,
        IOperationProgress? progress = null,
        CancellationToken cancellationToken = default,
        IScanFileSystem? fileSystem = null)
    {
        Traversal = traversal ?? ScanTraversalOptions.Default;
        Progress = progress;
        Statistics = new ScanTraversalStatistics();
        FileSystem = fileSystem ?? new RealScanFileSystem();

        if (Traversal.OperationTimeout is { } timeout && timeout > TimeSpan.Zero)
        {
            timeoutCts = new CancellationTokenSource(timeout);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        }
        else
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        CancellationToken = linkedCts.Token;
    }

    public ScanTraversalOptions Traversal { get; }

    public IOperationProgress? Progress { get; }

    public ScanTraversalStatistics Statistics { get; }

    public IScanFileSystem FileSystem { get; }

    public CancellationToken CancellationToken { get; }

    public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

    /// <summary>
    /// Waits for the retry delay while propagating cancellation (including timeout-linked tokens).
    /// </summary>
    public void WaitForRetryDelay()
    {
        var delay = Traversal.GetBoundedRetryDelay();
        if (delay <= TimeSpan.Zero)
        {
            CancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (CancellationToken.WaitHandle.WaitOne(delay))
        {
            CancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        linkedCts.Dispose();
        timeoutCts?.Dispose();
    }
}
