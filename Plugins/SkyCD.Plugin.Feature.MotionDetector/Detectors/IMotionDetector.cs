using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>One frame's detection result: the decision, a normalized score and the changed regions.</summary>
/// <param name="MotionDetected">True when the detector considers the frame to contain motion.</param>
/// <param name="Score">Normalized change measure in the range 0..1.</param>
/// <param name="Regions">Bounding rectangles of the changed areas in source-frame coordinates.</param>
internal readonly record struct DetectionOutcome(bool MotionDetected, double Score, IReadOnlyList<Rect> Regions);

/// <summary>
/// A motion detector that consumes grayscale frames from one source. Instances are stateful and must not be
/// shared between capture loops.
/// </summary>
internal interface IMotionDetector : IDisposable
{
    string Id { get; }

    /// <summary>Analyzes one frame and the grayscale conversion of the same frame.</summary>
    DetectionOutcome Process(Mat frame, Mat gray);
}
