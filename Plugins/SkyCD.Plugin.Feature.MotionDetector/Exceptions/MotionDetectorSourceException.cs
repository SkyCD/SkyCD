using System;

namespace SkyCD.Plugin.Feature.MotionDetector.Exceptions;

/// <summary>Raised when a capture source cannot be persisted into the open catalog.</summary>
public sealed class MotionDetectorSourceException(string label)
    : InvalidOperationException($"The capture source '{label}' could not be saved into the open catalog.");
