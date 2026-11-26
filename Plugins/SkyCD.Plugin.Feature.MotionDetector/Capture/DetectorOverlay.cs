using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>Draws the detector result over a preview frame so the user can judge framing before saving.</summary>
internal static class DetectorOverlay
{
    private static readonly Scalar RegionColor = new(0, 220, 0);
    private static readonly Scalar TextColor = new(255, 255, 255);
    private static readonly Scalar TextShadow = new(0, 0, 0);

    public static Mat Draw(Mat frame, string? detectorId, DetectionOutcome outcome)
    {
        var label = detectorId is null
            ? LocalizationService.Current.Get(MotionDetectorKeys.DetectorNone)
            : string.Create(System.Globalization.CultureInfo.CurrentCulture,
                $"{DetectorCatalog.Name(detectorId)}  {outcome.Score * 100:0.#}%");

        foreach (var region in outcome.Regions)
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                continue;
            }

            Cv2.Rectangle(frame, region, RegionColor, 2);
        }

        Cv2.PutText(frame, label, new Point(10, 26), HersheyFonts.HersheySimplex, 0.7, TextShadow, 3);
        Cv2.PutText(frame, label, new Point(10, 26), HersheyFonts.HersheySimplex, 0.7, TextColor, 1);
        return frame;
    }
}
