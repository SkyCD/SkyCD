using System;
using System.IO;
using SkyCD.Plugin.FileFormat.Mdf.Exceptions;

namespace SkyCD.Plugin.FileFormat.Mdf.Parsing;

/// <summary>
/// Resolves the two files of an Alcohol image: the MDS descriptor and the MDF data file. The plugin can
/// be opened from either file, so the partner is located by base name next to the opened file.
/// </summary>
internal static class MdfCompanion
{
    private const string DataExtension = ".mdf";
    private const string DescriptorExtension = ".mds";

    /// <summary>
    /// Returns the MDF data file for an opened MDS descriptor or MDF data file. Names are matched
    /// case-insensitively but never leave the opened file's directory.
    /// </summary>
    public static string ResolveImagePath(string? sourcePath, string? fileName)
    {
        var fullPath = RequireSourcePath(sourcePath, fileName);
        if (IsExtension(fullPath, DataExtension))
        {
            return fullPath;
        }

        return FindSibling(fullPath, DataExtension)
               ?? throw new MdfMissingImageException(ExpectedName(fullPath, DataExtension));
    }

    /// <summary>
    /// Returns the MDS descriptor next to an opened file, or <c>null</c> when the file was opened as an
    /// MDF and no descriptor accompanies it. A missing descriptor is tolerated because the data file
    /// carries the filesystem.
    /// </summary>
    public static string? ResolveDescriptorPath(string? sourcePath, string? fileName)
    {
        var fullPath = RequireSourcePath(sourcePath, fileName);
        if (IsExtension(fullPath, DescriptorExtension))
        {
            return fullPath;
        }

        return FindSibling(fullPath, DescriptorExtension);
    }

    private static string RequireSourcePath(string? sourcePath, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            return Path.GetFullPath(sourcePath);
        }

        throw new MdfImageFormatException(
            string.IsNullOrWhiteSpace(fileName) ? "no image path is available." : $"no image path is available for '{fileName}'.");
    }

    private static string? FindSibling(string fullPath, string extension)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var expectedName = ExpectedName(fullPath, extension);
        var candidate = Path.Combine(directory, expectedName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(file), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static string ExpectedName(string fullPath, string extension) =>
        Path.GetFileNameWithoutExtension(fullPath) + extension;

    private static bool IsExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
}
