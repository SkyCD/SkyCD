using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>Captures one annotated frame for the Add To List preview dialog.</summary>
internal static class CapturePreviewer
{
    private const int WarmupFrames = 12;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<SourcePreviewResult> CaptureAsync(SourceConfiguration source,
        SourceDetectorSettings settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            return await Task.Run(() => Capture(source, settings, timeout.Token), timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SourcePreviewResult.Failed(
                LocalizationService.Current.Get(MotionDetectorKeys.PreviewFailed));
        }
    }

    private static SourcePreviewResult Capture(SourceConfiguration source,
        SourceDetectorSettings settings, CancellationToken cancellationToken)
    {
        if (!CaptureConnection.TryOpen(source, out var connection, out var error) || connection is null)
        {
            return SourcePreviewResult.Failed(error ??
                LocalizationService.Current.Get(MotionDetectorKeys.SourceOpenFailed));
        }

        using (connection)
        using (var detector = DetectorCatalog.Create(settings.DetectorId, settings.Parameters))
        using (var frame = new Mat())
        using (var gray = new Mat())
        using (var best = new Mat())
        {
            var bestScore = -1.0;
            DetectionOutcome bestOutcome = default;
            for (var index = 0; index < WarmupFrames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!connection.Capture.Read(frame) || frame.Empty())
                {
                    break;
                }

                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                var outcome = detector?.Process(frame, gray)
                              ?? new DetectionOutcome(true, 0, Array.Empty<Rect>());
                if (index == 0 || outcome.Score > bestScore)
                {
                    bestScore = outcome.Score;
                    bestOutcome = outcome;
                    frame.CopyTo(best);
                }

                if (connection.FrameInterval > TimeSpan.FromMilliseconds(40))
                {
                    Thread.Sleep(connection.FrameInterval);
                }
            }

            if (best.Empty())
            {
                return SourcePreviewResult.Failed(
                    LocalizationService.Current.Get(MotionDetectorKeys.SourceStreamEnded));
            }

            DetectorOverlay.Draw(best, settings.DetectorId, bestOutcome);
            Cv2.ImEncode(".png", best, out var image);
            return SourcePreviewResult.Captured(image);
        }
    }
}
