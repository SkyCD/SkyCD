using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>One numeric parameter accepted by a detector, with the range the settings page enforces.</summary>
internal sealed record DetectorParameterSpec(string Key, double DefaultValue, double Minimum, double Maximum);

/// <summary>A detector identity plus the parameters it reads.</summary>
internal sealed record DetectorSpec(string Id, IReadOnlyList<DetectorParameterSpec> Parameters);

/// <summary>
/// The detector catalogue shared by the settings page, the capture loops and the preview. Names are localized
/// at call time so the page follows the app language without restarting the plugin.
/// </summary>
internal static class DetectorCatalog
{
    public const string FrameDifference = "frame-difference";
    public const string RunningAverage = "running-average";
    public const string Mog2 = "mog2";
    public const string Knn = "knn";
    public const string Farneback = "farneback";
    public const string EdgeChange = "edge-change";
    public const string PerceptualDifference = "perceptual-difference";

    private static readonly DetectorParameterSpec Threshold = new("threshold", 25, 1, 255);
    private static readonly DetectorParameterSpec MinimumArea = new("minAreaPercent", 0.2, 0, 100);
    private static readonly DetectorParameterSpec LearningRate = new("learningRate", 0.02, 0.001, 1);
    private static readonly DetectorParameterSpec History = new("history", 500, 10, 5000);

    public static IReadOnlyList<DetectorSpec> Specs { get; } =
    [
        new(FrameDifference, [Threshold, MinimumArea]),
        new(RunningAverage, [Threshold, LearningRate, MinimumArea]),
        new(Mog2,
        [
            History,
            new("varianceThreshold", 16, 1, 100),
            new("learningRate", 0.01, 0, 1),
            MinimumArea
        ]),
        new(Knn,
        [
            History,
            new("distanceThreshold", 400, 10, 2000),
            new("learningRate", 0.01, 0, 1),
            MinimumArea
        ]),
        new(Farneback, [new("magnitudeThreshold", 2, 0.1, 50), MinimumArea]),
        new(EdgeChange,
        [
            new("cannyLow", 50, 0, 255),
            new("cannyHigh", 150, 1, 255),
            new("changePercent", 5, 0.1, 100),
            MinimumArea
        ]),
        new(PerceptualDifference, [new("differencePercent", 2, 0.1, 100)])
    ];

    public static IReadOnlyList<SourceDetectorDescriptor> Describe()
    {
        var localizer = LocalizationService.Current;
        return Specs.Select(spec => new SourceDetectorDescriptor(
            spec.Id,
            localizer.Get(MotionDetectorKeys.DetectorName(spec.Id)),
            localizer.Get(MotionDetectorKeys.DetectorDescription(spec.Id)),
            spec.Parameters.Select(parameter => new SourceDetectorParameter(
                parameter.Key,
                localizer.Get(MotionDetectorKeys.ParameterName(parameter.Key)),
                parameter.DefaultValue,
                parameter.Minimum,
                parameter.Maximum)).ToArray())).ToArray();
    }

    /// <summary>Gets the default value for one parameter, falling back to 0 for unknown detectors or keys.</summary>
    public static double DefaultValue(string detectorId, string parameterKey) =>
        FindSpec(detectorId)?.Parameters.FirstOrDefault(parameter => parameter.Key == parameterKey)?.DefaultValue
        ?? 0;

    /// <summary>The normalized parameter map for a detector: stored values clamped, missing values defaulted.</summary>
    public static IReadOnlyDictionary<string, double> Normalize(string? detectorId,
        IReadOnlyDictionary<string, double>? stored)
    {
        var spec = FindSpec(detectorId);
        if (spec is null)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in spec.Parameters)
        {
            var value = stored is not null && stored.TryGetValue(parameter.Key, out var requested) &&
                        double.IsFinite(requested)
                ? requested
                : parameter.DefaultValue;
            values[parameter.Key] = Math.Clamp(value, parameter.Minimum, parameter.Maximum);
        }

        return values;
    }

    /// <summary>
    /// Creates a detector instance. A null or unknown id returns null, which the capture loop reads as
    /// "capture continuously": the legacy None option without a separate mode.
    /// </summary>
    public static IMotionDetector? Create(string? detectorId, IReadOnlyDictionary<string, double>? stored)
    {
        var parameters = new DetectorParameters(Normalize(detectorId, stored));
        return detectorId switch
        {
            FrameDifference => new FrameDifferenceDetector(parameters),
            RunningAverage => new RunningAverageDetector(parameters),
            Mog2 => new Mog2Detector(parameters),
            Knn => new KnnDetector(parameters),
            Farneback => new FarnebackDetector(parameters),
            EdgeChange => new EdgeChangeDetector(parameters),
            PerceptualDifference => new PerceptualDifferenceDetector(parameters),
            _ => null
        };
    }

    /// <summary>Gets the localized display name of a detector, or the id when it is unknown.</summary>
    public static string Name(string id) => LocalizationService.Current.Get(MotionDetectorKeys.DetectorName(id));

    private static DetectorSpec? FindSpec(string? detectorId) =>
        detectorId is null ? null : Specs.FirstOrDefault(spec => spec.Id == detectorId);
}
