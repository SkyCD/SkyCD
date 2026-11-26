using System;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>K-nearest-neighbour background model as implemented by OpenCV's KNN subtractor.</summary>
internal sealed class KnnDetector : IMotionDetector
{
    private readonly double minimumAreaPercent;
    private readonly double learningRate;
    private readonly BackgroundSubtractorKNN subtractor;
    private Mat? mask;

    public KnnDetector(DetectorParameters parameters)
    {
        var history = (int)parameters.Get("history", 500, 10, 5000);
        var distanceThreshold = parameters.Get("distanceThreshold", 400, 10, 2000);
        minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
        learningRate = parameters.Get("learningRate", 0.01, 0, 1);
        subtractor = BackgroundSubtractorKNN.Create(history, distanceThreshold, detectShadows: false);
    }

    public string Id => DetectorCatalog.Knn;

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
