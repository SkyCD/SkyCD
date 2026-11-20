using System;
using System.IO;
using SkyCD.Plugin.BinCue.Exceptions;

namespace SkyCD.Plugin.BinCue;

/// <summary>
/// Resolves the BIN data file that belongs to a CUE sheet. The sheet and the image are separate files,
/// so the track data is never contained in the source stream.
/// </summary>
internal static class BinCueCompanion
{
    /// <summary>
    /// Finds the BIN file next to the CUE sheet, using the sheet's FILE name or the sheet's own base name.
    /// Only simple file names are honored so a damaged or hostile sheet cannot point outside the sheet's
    /// directory.
    /// </summary>
    public static string ResolveImagePath(string? cuePath, CueFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var expectedName = ExpectedName(file.Name);
        if (string.IsNullOrWhiteSpace(cuePath))
        {
            throw new BinCueMissingImageException(expectedName);
        }

        var fullPath = Path.GetFullPath(cuePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new BinCueMissingImageException(expectedName);
        }

        if (string.IsNullOrWhiteSpace(expectedName))
        {
            expectedName = Path.GetFileNameWithoutExtension(fullPath) + ".bin";
        }

        var candidate = Path.Combine(directory, expectedName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        foreach (var found in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(found), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }
        }

        throw new BinCueMissingImageException(expectedName);
    }

    private static string ExpectedName(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(declared.Trim().Replace('\\', '/'));
        return name.Trim();
    }
}
