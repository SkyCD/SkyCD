using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Resolves the icon a freedesktop <c>.desktop</c> launcher names in its <c>Icon=</c> key.
/// </summary>
/// <remarks>
/// The value is either a path — absolute, relative to the launcher or a plain sibling file name — or a theme
/// name that is looked up in the icon folders shipped on the scanned volume, the way the launcher itself would.
/// Only a decodable PNG or JPEG inside the existing byte and pixel bounds is published, so a text file that
/// happens to be named <c>.png</c> produces no icon. The local path is host-owned: it is never logged, and the
/// diagnostic trace records only the kind of reason, never a path or an icon name.
/// </remarks>
internal static class LauncherIconReader
{
    private const int MaximumEntryBytes = 256 * 1024;
    private const int MaximumCandidateFiles = 32;
    private const int MaximumThemeDirectories = 32;
    private const string TraceCategory = "SkyCD.Plugin.Metadata.Executable";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];
    private static readonly string[] ThemeSubdirectories = ["apps", "mimetypes"];

    /// <summary>Decodes the launcher's icon; null when the entry or its icon cannot be resolved.</summary>
    public static async Task<ExecutableIconImage?> TryReadAsync(LocalMediaContext source,
        ExecutablePluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(source.Path))
        {
            return null;
        }

        string entryPath;
        try
        {
            entryPath = Path.GetFullPath(source.Path);
            var entry = new FileInfo(entryPath);
            if (!entry.Exists || entry.Length <= 0 || entry.Length > MaximumEntryBytes)
            {
                TraceUnresolved("the launcher entry is missing or larger than the entry bound");
                return null;
            }

            var text = await File.ReadAllTextAsync(entryPath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            var launcherDirectory = Path.GetDirectoryName(entryPath);
            if (string.IsNullOrEmpty(launcherDirectory))
            {
                return null;
            }

            var icon = ReadIconValue(text);
            if (icon is null)
            {
                TraceUnresolved("the launcher entry has no Icon key");
                return null;
            }

            var examined = 0;
            foreach (var candidate in EnumerateCandidates(icon, launcherDirectory))
            {
                if (examined >= MaximumCandidateFiles)
                {
                    break;
                }

                examined++;
                var image = TryDecode(candidate, options);
                if (image is not null)
                {
                    return image;
                }
            }

            TraceUnresolved(icon.Contains('/') || icon.Contains('\\') || Path.IsPathRooted(icon)
                ? "the icon path does not name a decodable image"
                : "the icon is a theme name that was not found in the volume's icon folders");
            return null;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            TraceUnresolved("the launcher entry could not be read");
            return null;
        }
    }

    /// <summary>
    /// Returns the first <c>Icon</c> value of the <c>[Desktop Entry]</c> group, ignoring localized
    /// <c>Icon[xx]</c> keys and icons declared by desktop actions.
    /// </summary>
    private static string? ReadIconValue(string text)
    {
        string? icon = null;
        var inEntryGroup = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inEntryGroup = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }

            if (!inEntryGroup)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || !string.Equals(line[..separator].Trim(), "Icon", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0)
            {
                icon = value;
            }
        }

        return icon;
    }

    /// <summary>
    /// Yields the files that could hold the icon: the named path first, then the sibling and theme lookups.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidates(string icon, string launcherDirectory)
    {
        var isPath = Path.IsPathRooted(icon) || icon.Contains('/') || icon.Contains('\\');
        if (isPath)
        {
            var path = Path.IsPathRooted(icon) ? icon : Path.Combine(launcherDirectory, icon);
            if (HasImageExtension(icon))
            {
                yield return path;
            }
            else
            {
                foreach (var extension in ImageExtensions)
                {
                    yield return path + extension;
                }
            }
        }
        else
        {
            if (HasImageExtension(icon))
            {
                yield return Path.Combine(launcherDirectory, icon);
            }

            foreach (var extension in ImageExtensions)
            {
                yield return Path.Combine(launcherDirectory, icon + extension);
            }
        }

        foreach (var candidate in EnumerateThemeCandidates(icon, launcherDirectory))
        {
            yield return candidate;
        }
    }

    /// <summary>Looks the icon name up in the icon folders under the volume the launcher lives on.</summary>
    private static IEnumerable<string> EnumerateThemeCandidates(string name, string launcherDirectory)
    {
        var root = FindVolumeRoot(launcherDirectory);
        if (root is null)
        {
            yield break;
        }

        var themes = string.Equals(name, "hicolor", StringComparison.Ordinal)
            ? new[] { "hicolor" }
            : new[] { name, "hicolor" };

        foreach (var dataRoot in new[] { Path.Combine("usr", "share"), "share" })
        {
            foreach (var theme in themes)
            {
                foreach (var candidate in EnumerateThemeDirectory(Path.Combine(root, dataRoot, "icons", theme), name))
                {
                    yield return candidate;
                }
            }

            foreach (var candidate in EnumerateThemeDirectory(Path.Combine(root, dataRoot, "pixmaps"), name))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateThemeDirectory(string directory, string name)
    {
        foreach (var file in EnumerateFiles(directory, name))
        {
            yield return file;
        }

        foreach (var sizeDirectory in ReadSizeDirectories(directory))
        {
            foreach (var file in EnumerateFiles(sizeDirectory, name))
            {
                yield return file;
            }

            foreach (var subdirectory in ThemeSubdirectories)
            {
                foreach (var file in EnumerateFiles(Path.Combine(sizeDirectory, subdirectory), name))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>Size folders from the largest down, so a detailed icon is preferred over a tiny one.</summary>
    private static string[] ReadSizeDirectories(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            return Directory.GetDirectories(directory)
                .OrderByDescending(static path => LeadingSize(Path.GetFileName(path)))
                .ThenByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
                .Take(MaximumThemeDirectories)
                .ToArray();
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string name)
    {
        foreach (var extension in ImageExtensions)
        {
            yield return Path.Combine(directory, name + extension);
        }
    }

    /// <summary>Walks up from the launcher until a folder that carries XDG icon data is found.</summary>
    private static string? FindVolumeRoot(string launcherDirectory)
    {
        var directory = launcherDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.Exists(Path.Combine(directory, "usr", "share", "icons")) ||
                Directory.Exists(Path.Combine(directory, "usr", "share", "pixmaps")) ||
                Directory.Exists(Path.Combine(directory, "share", "icons")) ||
                Directory.Exists(Path.Combine(directory, "share", "pixmaps")))
            {
                return directory;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent is null || string.Equals(parent, directory, StringComparison.Ordinal))
            {
                return null;
            }

            directory = parent;
        }

        return null;
    }

    private static ExecutableIconImage? TryDecode(string path, ExecutablePluginOptions options)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > options.MaxIconBytes)
            {
                return null;
            }

            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            if (!ExecutableIconDecoder.HasEncodedImageSignature(data))
            {
                return null;
            }

            return ExecutableIconDecoder.DecodeEncodedImage(data, ExecutablePluginOptions.MaximumIconPixels);
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is ExecutableIconException)
        {
            return null;
        }
    }

    private static bool HasImageExtension(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static int LeadingSize(string name)
    {
        var digits = 0;
        while (digits < name.Length && char.IsAsciiDigit(name[digits]))
        {
            digits++;
        }

        return digits > 0 && int.TryParse(name.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture,
            out var size)
            ? size
            : -1;
    }

    private static void TraceUnresolved(string reason) =>
        Trace.TraceInformation("{0}: no launcher icon: {1}.", TraceCategory, reason);

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;
}
