using System;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Per-pixel Gaussian mixture background model as implemented by OpenCV's MOG2 subtractor.</summary>
internal sealed class Mog2Detector : IMotionDetector
{
    private readonly double minimumAreaPercent;
    private readonly double learningRate;
    private readonly BackgroundSubtractorMOG2 subtractor;
    private Mat? mask;

    public Mog2Detector(DetectorParameters parameters)
    {
        var history = (int)parameters.Get("history", 500, 10, 5000);
        var varianceThreshold = parameters.Get("varianceThreshold", 16, 1, 100);
        minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
        learningRate = parameters.Get("learningRate", 0.01, 0, 1);
        subtractor = BackgroundSubtractorMOG2.Create(history, varianceThreshold, detectShadows: false);
    }

    public string Id => DetectorCatalog.Mog2;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        mask ??= new Mat();
        subtractor.Apply(gray, mask, learningRate);
        return DetectionRegions.FromMask(mask, minimumAreaPercent);
    }

    public void Dispose()
    {
        subtractor.Dispose();
        mask?.Dispose();
    }
}
