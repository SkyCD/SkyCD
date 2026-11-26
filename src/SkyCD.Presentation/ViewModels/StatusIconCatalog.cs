using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// String-only registry of status-variant icon pack keys (issue #1226). The pack-enum reflection
/// and brush construction now live behind the App-level <c>StatusIconResolver</c>, where Avalonia
/// and IconPacks are declared dependencies; view models only pass the pack/key and color strings
/// around. Keys are registered once at host startup before any icon picker opens.
/// </summary>
public static class StatusIconCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<string> keys = Array.Empty<string>();

    public static void RegisterKeys(IEnumerable<string> iconKeys)
    {
        ArgumentNullException.ThrowIfNull(iconKeys);
        lock (Gate)
        {
            keys = iconKeys
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Select(static key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static IReadOnlyList<string> GetAllKeys()
    {
        lock (Gate)
        {
            return keys;
        }
    }

    public static bool IsKnownKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();
        lock (Gate)
        {
            return keys.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
        }
    }
}
