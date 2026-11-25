using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Factories;

/// <summary>
/// One square PNG frame extracted from a plugin's embedded ICO, with its pixel size.
/// </summary>
public sealed record PluginIconFrame(int Size, byte[] Png)
{
    /// <summary>
    /// Returns the frame closest to the requested render size: an exact match wins, otherwise the
    /// smallest distance wins and ties prefer the larger frame, which downscales more cleanly.
    /// Returns <see langword="null"/> when no frame is available.
    /// </summary>
    public static byte[]? SelectNearest(IEnumerable<PluginIconFrame> frames, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var target = Math.Max(1, targetSize);
        PluginIconFrame? best = null;
        foreach (var frame in frames)
        {
            if (best is null)
            {
                best = frame;
                continue;
            }

            var distance = Math.Abs(frame.Size - target);
            var bestDistance = Math.Abs(best.Size - target);
            if (distance < bestDistance || (distance == bestDistance && frame.Size > best.Size))
            {
                best = frame;
            }
        }

        return best?.Png;
    }
}
