using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Daa.Models;

/// <summary>
/// Normalizes an entry path read from a DAA image and rejects traversal or absolute paths.
/// </summary>
internal static class DaaEntryPath
{
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var raw = path.Replace('\\', '/');
        if (raw.StartsWith('/'))
        {
            return false;
        }

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var versionSeparator = part.IndexOf(';');
            var name = versionSeparator >= 0 ? part[..versionSeparator] : part;
            if (name.Length == 0 || name == "." || name == "..")
            {
                return false;
            }

            if (name.Any(char.IsControl))
            {
                return false;
            }

            result.Add(name.ToUpperInvariant());
        }

        if (result.Count == 0)
        {
            return false;
        }

        normalized = string.Join('/', result);
        return true;
    }
}
