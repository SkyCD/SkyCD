using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>
/// One configured source's capture loop: opens the stream, runs the detector, writes motion frames into the
/// physical destination folder and lets <see cref="DestinationWatcher"/> hand them to the catalog. The loop
/// always runs on a worker task and waits between frames, so it never blocks the UI thread or busy-polls.
/// </summary>
internal sealed class CaptureSourceRunner : IDisposable
{
    private static readonly TimeSpan MinimumSaveInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(1);

    private readonly CaptureSourceConfiguration source;
    private readonly Func<CaptureDetectorSettings> settings;
    private readonly ICaptureSourceHostApi host;
    private readonly Action<string> report;
    private readonly CancellationTokenSource cancellation = new();
    private DestinationWatcher? watcher;
    private CaptureConnection? connection;
    private Task? loop;
    private int started;
    private string? lastReported;

    public CaptureSourceRunner(CaptureSourceConfiguration source, Func<CaptureDetectorSettings> settings,
        ICaptureSourceHostApi host, Action<string> report)
    {
        this.source = source;
        this.settings = settings;
        this.host = host;
        this.report = report;
    }

    public string SourceId => source.SourceId;

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 1)
        {
            return;
        }

        try
        {
            watcher = new DestinationWatcher(source, host, Report);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            Interlocked.Exchange(ref started, 0);
            Report(LocalizationService.Current.Format(MotionDetectorKeys.DestinationMissing,
                source.DestinationFolder));
            return;
        }

        loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        var current = settings();
        using var detector = DetectorCatalog.Create(current.DetectorId, current.Parameters);
        while (!cancellation.IsCancellationRequested)
        {
            if (!host.IsCatalogOpen)
            {
                await DelayAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                continue;
            }

            if (!CaptureConnection.TryOpen(source, out var opened, out var error) || opened is null)
            {
                Report(error ?? LocalizationService.Current.Get(MotionDetectorKeys.SourceOpenFailed));
                await DelayAsync(ReconnectDelay).ConfigureAwait(false);
                continue;
            }

            Interlocked.Exchange(ref connection, opened);
            try
            {
                await ReadFramesAsync(opened, detector).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref connection, null)?.Dispose();
            }

            await DelayAsync(source.KindId == CaptureSourceKinds.File ? LoopDelay : ReconnectDelay)
                .ConfigureAwait(false);
        }
    }

    private async Task ReadFramesAsync(CaptureConnection opened, IMotionDetector? detector)
    {
        using var frame = new Mat();
        using var gray = new Mat();
        var lastSaved = DateTimeOffset.MinValue;
        var interval = opened.FrameInterval;
        while (!cancellation.IsCancellationRequested && host.IsCatalogOpen)
        {
            if (!opened.Capture.Read(frame) || frame.Empty())
            {
                Report(LocalizationService.Current.Get(MotionDetectorKeys.SourceStreamEnded));
                return;
            }

            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            var captured = DateTimeOffset.Now;
            var detected = detector?.Process(frame, gray).MotionDetected ?? true;
            if (detected && captured - lastSaved >= MinimumSaveInterval &&
                !string.IsNullOrWhiteSpace(FrameWriter.TryWrite(frame, source.DestinationFolder, captured)))
            {
                lastSaved = captured;
            }

            await DelayAsync(interval).ConfigureAwait(false);
        }
    }

    private async Task DelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    /// <summary>Reports a message only when it differs from the previous one, so a dead camera cannot spam.</summary>
    private void Report(string message)
    {
        if (string.Equals(lastReported, message, StringComparison.Ordinal))
        {
            return;
        }

        lastReported = message;
        report(message);
    }

    public async Task StopAsync()
    {
        cancellation.Cancel();
        watcher?.Dispose();
        Interlocked.Exchange(ref connection, null)?.Dispose();
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                // A blocked native read is abandoned; the process is shutting the source down.
            }
        }
    }

    public void Dispose() => cancellation.Dispose();
}
