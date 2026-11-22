using System;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

/// <summary>
/// Maps v5 thumbnail keys to the four-level hexadecimal object store used by .dcmd packages.
/// </summary>
public static class DiskCatalogMakerV5ThumbnailResolver
{
    /// <summary>
    /// Returns a normalized extension (leading dot, ASCII letters/digits only) or null when unusable.
    /// </summary>
    public static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var trimmed = extension.Trim();
        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length is 0 or > 16)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return null;
            }
        }

        return "." + trimmed;
    }

    /// <summary>
    /// Builds the package-relative thumbnail path for a key: Thumbnails/aa/bb/cc/dd.ext.
    /// </summary>
    public static string CreateRelativePath(uint key, string? extension)
    {
        var builder = new StringBuilder("Thumbnails/");
        AppendHexPair(builder, (byte)(key >> 24));
        builder.Append('/');
        AppendHexPair(builder, (byte)(key >> 16));
        builder.Append('/');
        AppendHexPair(builder, (byte)(key >> 8));
        builder.Append('/');
        AppendHexPair(builder, (byte)key);
        builder.Append(NormalizeExtension(extension));
        return builder.ToString();
    }

    /// <summary>
    /// Resolves a key against a package root. The relative path is always preserved;
    /// the method reports whether the actual object exists.
    /// </summary>
    public static bool TryResolve(
        string? packageRoot,
        uint key,
        string? extension,
        out string relativePath,
        out string? fullPath)
    {
        relativePath = CreateRelativePath(key, extension);
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            fullPath = null;
            return false;
        }

        fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath);
    }

    private static void AppendHexPair(StringBuilder builder, byte value)
    {
        builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
    }
}
