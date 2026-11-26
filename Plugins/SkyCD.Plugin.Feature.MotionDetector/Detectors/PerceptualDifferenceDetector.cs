using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Cheap perceptual difference over a 32x32 grayscale reduction, suitable for low-CPU cameras.</summary>
internal sealed class PerceptualDifferenceDetector(DetectorParameters parameters) : IMotionDetector
{
    private const int ReducedSize = 32;

    private readonly double differencePercent = parameters.Get("differencePercent", 2, 0.1, 100);
    private Mat? previous;
    private Mat? difference;

    public string Id => DetectorCatalog.PerceptualDifference;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(ReducedSize, ReducedSize), interpolation: InterpolationFlags.Area);
        if (previous is null)
        {
            previous = small.Clone();
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        difference ??= new Mat();
        Cv2.Absdiff(previous, small, difference);
        small.CopyTo(previous);
        var score = Cv2.Mean(difference).Val0 / 255.0;
        if (score * 100 < differencePercent)
        {
            return new DetectionOutcome(false, score, Array.Empty<Rect>());
        }

        IReadOnlyList<Rect> regions = [new Rect(0, 0, frame.Width, frame.Height)];
        return new DetectionOutcome(true, score, regions);
    }

    public void Dispose()
    {
        previous?.Dispose();
        difference?.Dispose();
    }
}
