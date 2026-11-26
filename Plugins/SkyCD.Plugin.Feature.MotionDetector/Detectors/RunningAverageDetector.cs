using System;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Running average background model, with the deviation from the mean thresholded into a mask.</summary>
internal sealed class RunningAverageDetector(DetectorParameters parameters) : IMotionDetector
{
    private readonly double threshold = parameters.Get("threshold", 25, 1, 255);
    private readonly double learningRate = parameters.Get("learningRate", 0.02, 0.001, 1);
    private readonly double minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
    private readonly Mat emptyMask = new();
    private Mat? background;
    private Mat? difference;
    private Mat? mask;

    public string Id => DetectorCatalog.RunningAverage;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        if (background is null || background.Size() != gray.Size())
        {
            background?.Dispose();
            background = gray.Clone();
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        difference ??= new Mat();
        mask ??= new Mat();
        Cv2.Absdiff(background, gray, difference);
        Cv2.AccumulateWeighted(gray, background, learningRate, emptyMask);
        Cv2.Threshold(difference, mask, threshold, 255, ThresholdTypes.Binary);
        return DetectionRegions.FromMask(mask, minimumAreaPercent);
    }

    public void Dispose()
    {
        background?.Dispose();
        difference?.Dispose();
        mask?.Dispose();
        emptyMask.Dispose();
    }
}
