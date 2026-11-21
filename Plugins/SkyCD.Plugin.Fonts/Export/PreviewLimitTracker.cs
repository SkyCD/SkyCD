using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Fonts.Export;

/// <summary>
/// Counts generated previews per opaque group token, so an optional per-folder limit is enforced inside the
/// provider that produces previews. Counts are process-wide, bounded in memory and reset whenever the
/// relevant settings change, which is the only point where earlier generations stop applying.
/// </summary>
internal sealed class PreviewLimitTracker
{
    private const string NoGroup = "\u0000";

    private readonly object gate = new();
    private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);
    private string signature = string.Empty;

    /// <summary>Reserves one preview for the group, or returns <c>false</c> when the limit is already reached.</summary>
    public bool TryReserve(string? groupKey, int limit, string settingsSignature)
    {
        lock (gate)
        {
            if (!string.Equals(signature, settingsSignature, StringComparison.Ordinal))
            {
                counts.Clear();
                signature = settingsSignature;
            }

            if (limit <= 0)
            {
                return true;
            }

            var key = groupKey ?? NoGroup;
            counts.TryGetValue(key, out var current);
            if (current >= limit)
            {
                return false;
            }

            if (counts.Count >= 4096 && !counts.ContainsKey(key))
            {
                counts.Clear();
            }

            counts[key] = current + 1;
            return true;
        }
    }

    /// <summary>Gives back a reservation whose preview could not be produced.</summary>
    public void Release(string? groupKey)
    {
        lock (gate)
        {
            var key = groupKey ?? NoGroup;
            if (counts.TryGetValue(key, out var current) && current > 0)
            {
                counts[key] = current - 1;
            }
        }
    }
}
