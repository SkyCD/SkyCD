using System;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Canny edge change ratio: the fraction of edge pixels that appear or disappear between two frames.</summary>
internal sealed class EdgeChangeDetector(DetectorParameters parameters) : IMotionDetector
{
    private readonly double cannyLow = parameters.Get("cannyLow", 50, 0, 255);
    private readonly double cannyHigh = parameters.Get("cannyHigh", 150, 1, 255);
    private readonly double changePercent = parameters.Get("changePercent", 5, 0.1, 100);
    private readonly double minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
    private Mat? previousEdges;
    private Mat? edges;
    private Mat? changed;

    public string Id => DetectorCatalog.EdgeChange;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        edges ??= new Mat();
        changed ??= new Mat();
        Cv2.Canny(gray, edges, cannyLow, cannyHigh);
        if (previousEdges is null || previousEdges.Size() != edges.Size())
        {
            previousEdges?.Dispose();
            previousEdges = edges.Clone();
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        var previousCount = Cv2.CountNonZero(previousEdges);
        var currentCount = Cv2.CountNonZero(edges);
        Cv2.Absdiff(previousEdges, edges, changed);
        edges.CopyTo(previousEdges);
        var changeCount = Cv2.CountNonZero(changed);
        var denominator = Math.Max(1, previousCount + currentCount);
        var score = changeCount / (double)denominator;
        var regions = DetectionRegions.FromMask(changed, minimumAreaPercent);
        return new DetectionOutcome(score * 100 >= changePercent && changeCount > 0, score, regions.Regions);
    }

    public void Dispose()
    {
        previousEdges?.Dispose();
        edges?.Dispose();
        changed?.Dispose();
    }
}
