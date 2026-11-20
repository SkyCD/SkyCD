using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Rar;

/// <summary>
/// Locates companion volumes of a multi-volume RAR set so the plugin can refuse the set with a typed
/// diagnostic instead of failing with an opaque reader error or silently indexing only the first volume.
/// </summary>
internal static class RarVolumeCompanions
{
    private const string NewStyleMarker = ".part";
    private const string RarExtension = ".rar";

    /// <summary>
    /// Returns the full path of a companion volume stored next to <paramref name="sourcePath"/>, or
    /// <c>null</c> when the source does not look like part of a multi-volume set.
    /// </summary>
    public static string? FindCompanion(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            foreach (var candidate in CompanionNames(Path.GetFileName(sourcePath)))
            {
                var path = Path.Combine(directory, candidate);
                if (!string.Equals(path, sourcePath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>Builds the file names a multi-volume RAR source would have for its other volumes.</summary>
    public static IEnumerable<string> CompanionNames(string fileName)
    {
        if (!fileName.EndsWith(RarExtension, StringComparison.OrdinalIgnoreCase))
        {
            // Old style continuation volumes: archive.r00, archive.r01, ...
            var simpleName = StripExtension(fileName);
            return
            [
                simpleName + ".r00",
                simpleName + ".R00"
            ];
        }

        var marker = fileName.LastIndexOf(NewStyleMarker, StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
        {
            var digits = fileName.AsSpan(marker + NewStyleMarker.Length,
                fileName.Length - marker - NewStyleMarker.Length - RarExtension.Length);
            if (!IsAsciiDigits(digits))
            {
                return [];
            }

            var baseName = fileName[..marker];
            var number = int.Parse(digits, CultureInfo.InvariantCulture);
            var next = (number + 1).ToString(CultureInfo.InvariantCulture);
            var names = new List<string>(6);
            if (number > 1)
            {
                names.Add(baseName + ".part" + (number - 1).ToString(CultureInfo.InvariantCulture) + RarExtension);
            }

            names.Add(baseName + ".part" + next + RarExtension);
            names.Add(baseName + ".PART" + next + RarExtension);
            names.Add(baseName + RarExtension);
            names.Add(baseName + ".r00");
            names.Add(baseName + ".R00");
            return names;
        }

        var stem = StripExtension(fileName);
        return
        [
            stem + NewStyleMarker + "1" + RarExtension,
            stem + ".PART1" + RarExtension,
            stem + ".r00",
            stem + ".R00"
        ];
    }

    private static string StripExtension(string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return index > 0 ? fileName[..index] : fileName;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        if (value.Length is 0 or > 4)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
