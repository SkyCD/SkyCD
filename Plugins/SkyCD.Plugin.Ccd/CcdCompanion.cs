using System;
using System.IO;
using SkyCD.Plugin.Ccd.Exceptions;

namespace SkyCD.Plugin.Ccd;

/// <summary>
/// Resolves the IMG data file that belongs to a CCD descriptor. CloneCD stores the descriptor and the
/// track data in separate files, so the data file is never contained in the source stream.
/// </summary>
internal static class CcdCompanion
{
    /// <summary>
    /// Finds the IMG file next to the descriptor, using an optional <c>ImageFile</c> entry from the
    /// descriptor or the descriptor's own base name. Only simple file names are honored so a damaged or
    /// hostile descriptor cannot point outside the descriptor's directory.
    /// </summary>
    public static string ResolveImagePath(string? descriptorPath, CcdDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptorPath))
        {
            var name = ExpectedName(null, descriptor);
            throw new CcdMissingImageException(name.Length == 0 ? "image.img" : name);
        }

        var fullPath = Path.GetFullPath(descriptorPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new CcdMissingImageException(ExpectedName(fullPath, descriptor));
        }

        var expectedName = ExpectedName(fullPath, descriptor);
        if (expectedName.Length == 0)
        {
            throw new CcdMissingImageException("image.img");
        }

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

        throw new CcdMissingImageException(expectedName);
    }

    private static string ExpectedName(string? descriptorPath, CcdDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.ImageFileName))
        {
            var declared = Path.GetFileName(descriptor.ImageFileName.Trim());
            if (!string.IsNullOrWhiteSpace(declared))
            {
                return declared;
            }
        }

        if (string.IsNullOrWhiteSpace(descriptorPath))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(descriptorPath) + ".img";
    }
}
