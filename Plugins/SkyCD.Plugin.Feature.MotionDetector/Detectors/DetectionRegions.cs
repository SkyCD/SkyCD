using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Reads numeric detector parameters with bounds so a stale or hand-edited value cannot break capture.</summary>
internal sealed class DetectorParameters(IReadOnlyDictionary<string, double>? values)
{
    public double Get(string key, double fallback, double minimum, double maximum)
    {
        var value = values is not null && values.TryGetValue(key, out var stored) && double.IsFinite(stored)
            ? stored
            : fallback;
        return Math.Clamp(value, minimum, maximum);
    }
}

/// <summary>Shared mask scoring and region extraction for the detectors that threshold a binary image.</summary>
internal static class DetectionRegions
{
    private const int MaxRegions = 8;

    public static DetectionOutcome FromMask(Mat mask, double minimumAreaPercent)
    {
        var area = mask.Width * (long)mask.Height;
        if (area <= 0)
        {
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        var changed = Cv2.CountNonZero(mask);
        var score = changed / (double)area;
        var minimumFraction = Math.Clamp(minimumAreaPercent, 0, 100) / 100.0;
        var regions = changed == 0 ? Array.Empty<Rect>() : FindRegions(mask, minimumFraction * area);
        return new DetectionOutcome(score >= minimumFraction && changed > 0, score, regions);
    }

    private static IReadOnlyList<Rect> FindRegions(Mat mask, double minimumArea)
    {
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        var regions = new List<Rect>(Math.Min(contours.Length, MaxRegions));
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width * (long)bounds.Height < minimumArea)
            {
                continue;
            }

            regions.Add(bounds);
            if (regions.Count == MaxRegions)
            {
                break;
            }
        }

        return regions;
    }
}
