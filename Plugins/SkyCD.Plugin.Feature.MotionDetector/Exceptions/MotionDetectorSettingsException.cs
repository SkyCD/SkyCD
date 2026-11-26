using System;

namespace SkyCD.Plugin.Feature.MotionDetector.Exceptions;

/// <summary>Raised when detector settings or plugin state need an open catalog that is not available.</summary>
public sealed class MotionDetectorSettingsException()
    : InvalidOperationException("Motion detector settings require an open catalog.");
