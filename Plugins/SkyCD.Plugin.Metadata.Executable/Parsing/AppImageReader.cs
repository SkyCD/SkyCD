using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Reads the app icon an AppImage carries in the ISO 9660 volume appended to its runtime.
/// </summary>
/// <remarks>
/// An AppImage is an ELF runtime followed by the packed application directory. The type byte at offset ten
/// says which filesystem the payload uses: type 1 is ISO 9660, which is read here through the supplied bounded
/// stream, and type 2 is squashfs, which has no reader with a compliant licence and is therefore reported as
/// "no icon" rather than guessed. The archive is never mounted, extracted to disk or executed; only the icon
/// file and the volume's directory records are read.
///
/// The icon is the root <c>.DirIcon</c> entry, which is usually a Rock Ridge symbolic link to a PNG at the root,
/// so links are followed inside the volume with a hop limit. When the link cannot be resolved, the largest
/// PNG or JPEG under the root icon folders is used as a best-effort substitute. Every failure is reported as
/// "no icon"; a damaged AppImage never fails the indexing pass.
/// </remarks>
internal static class AppImageReader
{
    private const int ElfMagicBytes = 4;
    private const int SignatureOffset = 8;
    private const byte Iso9660Type = 1;

    private const int IsoSectorBytes = 2048;
    private const int PrimaryVolumeDescriptorSector = 16;
    private const int ProbeStepBytes = 512 * 1024;
    private const int MaximumProbeBytes = 4 * 1024 * 1024;

    private const int MaximumLinkBytes = 512;
    private const int MaximumLinkHops = 8;
    private const int MaximumPathCharacters = 1024;
    private const int MaximumDirectories = 48;
    private const int MaximumIconFiles = 64;
    private const int MaximumSearchDepth = 4;

    private static ReadOnlySpan<byte> ElfMagic => [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    private static ReadOnlySpan<byte> PrimaryVolumeDescriptorSignature =>
        [0x01, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01];

    /// <summary>True when the header starts an AppImage of either payload type.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> header) =>
        header.Length >= SignatureOffset + 3 &&
        header[..ElfMagicBytes].SequenceEqual(ElfMagic) &&
        header[SignatureOffset] == (byte)'A' &&
        header[SignatureOffset + 1] == (byte)'I' &&
        header[SignatureOffset + 2] is Iso9660Type or 2;

    /// <summary>
    /// Decodes the app icon of a type 1 AppImage; null for any other payload, an unresolvable entry or a
    /// damaged volume.
    /// </summary>
    public static async Task<ExecutableIconImage?> TryReadAsync(ExecutableSource source,
        ExecutablePluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        if (source.Length < SignatureOffset + 3)
        {
            return null;
        }

        byte[] header;
        try
        {
            header = await source.ReadAsync(0, SignatureOffset + 3, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutableReadException)
        {
            return null;
        }

        if (!HasSignature(header) || header[SignatureOffset + 2] != Iso9660Type)
        {
            return null;
        }

        var volumeStart = await FindVolumeStartAsync(source, cancellationToken).ConfigureAwait(false);
        if (volumeStart is not { } start || start >= source.Length)
        {
            return null;
        }

        try
        {
            using var stream = new ExecutableSourceStream(source, cancellationToken, start, source.Length - start);
            using var volume = new CDReader(stream, joliet: true, hideVersions: true);
            return ReadDirIcon(volume, options) ?? ReadLargestIcon(volume, options);
        }
        catch (Exception exception) when (IsVolumeFailure(exception) || exception is ExecutableIconException)
        {
            return null;
        }
    }

    /// <summary>
    /// Locates the start of the ISO 9660 volume by its primary volume descriptor, which always sits sixteen
    /// sectors into the volume. Only the beginning of the file is probed, because every AppImage runtime is
    /// far smaller than the probe bound.
    /// </summary>
    private static async Task<long?> FindVolumeStartAsync(ExecutableSource source, CancellationToken cancellationToken)
    {
        var limit = (long)Math.Min(source.Length, MaximumProbeBytes);
        var probe = new byte[limit];
        var first = (int)Math.Min(limit, ProbeStepBytes);
        var head = await source.ReadAsync(0, first, cancellationToken).ConfigureAwait(false);
        head.CopyTo(probe.AsSpan(0, first));
        var found = FindPrimaryVolumeDescriptor(probe.AsSpan(0, first));
        if (found is not null || limit <= first)
        {
            return found;
        }

        var rest = await source.ReadAsync(first, (int)(limit - first), cancellationToken).ConfigureAwait(false);
        rest.CopyTo(probe.AsSpan(first));
        return FindPrimaryVolumeDescriptor(probe.AsSpan());
    }

    private static long? FindPrimaryVolumeDescriptor(ReadOnlySpan<byte> probe)
    {
        for (var index = 0; index + PrimaryVolumeDescriptorSignature.Length <= probe.Length; index++)
        {
            if (probe[index] != 0x01)
            {
                continue;
            }

            if (!probe.Slice(index, PrimaryVolumeDescriptorSignature.Length)
                    .SequenceEqual(PrimaryVolumeDescriptorSignature))
            {
                continue;
            }

            var volumeStart = index - ((long)PrimaryVolumeDescriptorSector * IsoSectorBytes);
            if (volumeStart >= 0)
            {
                return volumeStart;
            }
        }

        return null;
    }

    private static ExecutableIconImage? ReadDirIcon(CDReader volume, ExecutablePluginOptions options)
    {
        foreach (var path in new[] { ".DirIcon", "/.DirIcon", "\\.DirIcon" })
        {
            var data = ReadEntry(volume, path, options.MaxIconBytes);
            if (data is null || !ExecutableIconDecoder.HasEncodedImageSignature(data))
            {
                continue;
            }

            try
            {
                return ExecutableIconDecoder.DecodeEncodedImage(data, ExecutablePluginOptions.MaximumIconPixels);
            }
            catch (ExecutableIconException)
            {
                // A damaged entry only removes this candidate.
            }
        }

        return null;
    }

    /// <summary>
    /// Follows symlinks inside the volume and returns the entry's bytes, bounded by the icon byte limit.
    /// </summary>
    private static byte[]? ReadEntry(CDReader volume, string path, int maximumBytes)
    {
        for (var hop = 0; hop <= MaximumLinkHops; hop++)
        {
            try
            {
                if (!volume.FileExists(path))
                {
                    return null;
                }

                if (IsSymbolicLink(volume, path))
                {
                    var target = ReadLinkTarget(volume, path);
                    var resolved = target is null ? null : ResolveLinkTarget(path, target);
                    if (resolved is null)
                    {
                        return null;
                    }

                    path = resolved;
                    continue;
                }

                var length = volume.GetFileLength(path);
                if (length <= 0 || length > maximumBytes)
                {
                    return null;
                }

                using var stream = volume.OpenFile(path, FileMode.Open, FileAccess.Read);
                var data = new byte[length];
                stream.ReadExactly(data);
                return data;
            }
            catch (Exception exception) when (IsVolumeFailure(exception))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the entry is a Rock Ridge symbolic link. A volume without Rock Ridge data cannot tell links
    /// apart, so its entries are read as regular files instead of failing the whole volume.
    /// </summary>
    private static bool IsSymbolicLink(CDReader volume, string path)
    {
        try
        {
            return volume.GetUnixFileInfo(path).FileType == UnixFileType.Link;
        }
        catch (Exception exception) when (IsVolumeFailure(exception))
        {
            return false;
        }
    }

    private static string? ReadLinkTarget(CDReader volume, string path)
    {
        using var stream = volume.OpenFile(path, FileMode.Open, FileAccess.Read);
        var buffer = new byte[MaximumLinkBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read <= 0)
        {
            return null;
        }

        var target = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\0', ' ', '\r', '\n');
        return target.Length is 0 or > MaximumPathCharacters ? null : target;
    }

    /// <summary>
    /// Resolves a link target against the link's folder, refusing anything that leaves the volume root.
    /// </summary>
    private static string? ResolveLinkTarget(string linkPath, string target)
    {
        var separator = Path.DirectorySeparatorChar;
        var segments = new List<string>();
        if (!target.StartsWith('/'))
        {
            var slash = linkPath.LastIndexOfAny(['/', '\\', separator]);
            if (slash > 0)
            {
                segments.AddRange(linkPath[..slash].Split(['/', '\\', separator],
                    StringSplitOptions.RemoveEmptyEntries));
            }
        }

        foreach (var segment in target.Split(['/', '\\', separator], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : $"{separator}{string.Join(separator, segments)}";
    }

    /// <summary>
    /// Best-effort fallback for an unresolvable <c>.DirIcon</c>: the largest PNG or JPEG in the volume's icon
    /// folders, decoded only if it is a real image within the pixel bound.
    /// </summary>
    private static ExecutableIconImage? ReadLargestIcon(CDReader volume, ExecutablePluginOptions options)
    {
        var candidates = new List<IconFile>();
        AddIcons(volume, string.Empty, candidates, options);
        foreach (var root in IconRoots)
        {
            var directories = 0;
            CollectIcons(volume, root, 0, candidates, ref directories, options);
        }

        foreach (var candidate in candidates.OrderByDescending(static file => file.Length)
                     .Take(options.MaxIndexedIconImages))
        {
            var data = ReadEntry(volume, candidate.Path, options.MaxIconBytes);
            if (data is null || !ExecutableIconDecoder.HasEncodedImageSignature(data))
            {
                continue;
            }

            try
            {
                return ExecutableIconDecoder.DecodeEncodedImage(data, ExecutablePluginOptions.MaximumIconPixels);
            }
            catch (ExecutableIconException)
            {
                // A damaged candidate only removes itself.
            }
        }

        return null;
    }

    private static void CollectIcons(CDReader volume, string path, int depth, List<IconFile> candidates,
        ref int directories, ExecutablePluginOptions options)
    {
        if (depth > MaximumSearchDepth || directories >= MaximumDirectories || candidates.Count >= MaximumIconFiles)
        {
            return;
        }

        directories++;
        AddIcons(volume, path, candidates, options);

        string[] subdirectories;
        try
        {
            subdirectories = volume.GetDirectories(path);
        }
        catch (Exception exception) when (IsVolumeFailure(exception))
        {
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            CollectIcons(volume, subdirectory, depth + 1, candidates, ref directories, options);
        }
    }

    private static void AddIcons(CDReader volume, string path, List<IconFile> candidates,
        ExecutablePluginOptions options)
    {
        string[] files;
        try
        {
            files = volume.GetFiles(path);
        }
        catch (Exception exception) when (IsVolumeFailure(exception))
        {
            return;
        }

        foreach (var file in files)
        {
            if (candidates.Count >= MaximumIconFiles || !IsImageName(file))
            {
                continue;
            }

            try
            {
                var length = volume.GetFileLength(file);
                if (length > 0 && length <= options.MaxIconBytes)
                {
                    candidates.Add(new IconFile(file, length));
                }
            }
            catch (Exception exception) when (IsVolumeFailure(exception))
            {
                // A single unreadable entry only removes itself.
            }
        }
    }

    private static bool IsImageName(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private static string[] IconRoots { get; } =
    [
        Path.Combine("usr", "share", "icons"),
        Path.Combine("usr", "share", "pixmaps")
    ];

    private static bool IsVolumeFailure(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or FormatException or OverflowException
            or InvalidFileSystemException or ExecutableReadException;

    private readonly record struct IconFile(string Path, long Length);
}
