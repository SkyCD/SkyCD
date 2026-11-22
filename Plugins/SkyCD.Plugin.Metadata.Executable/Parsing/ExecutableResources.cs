using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>One leaf resource of the image: its type, numeric name, language and payload location.</summary>
internal readonly record struct ExecutableResource(int Type, int Name, int Language, long DataOffset,
    int Length);

/// <summary>
/// The icon, cursor and version entries of an executable's resource directory.
/// </summary>
/// <remarks>
/// Only the fixed-size directory nodes are read, so locating the icon entries costs a few kilobytes of source
/// for any real program. Resource types the plugin has no use for are skipped without being read, the walk is
/// depth-limited by construction and every payload length is checked against the section data before it is
/// handed on.
/// </remarks>
internal sealed class ExecutableResources
{
    /// <summary>Predefined resource type: cursor.</summary>
    public const int Cursor = 1;

    /// <summary>Predefined resource type: icon.</summary>
    public const int Icon = 3;

    /// <summary>Predefined resource type: icon group.</summary>
    public const int IconGroup = 14;

    /// <summary>Predefined resource type: version information.</summary>
    public const int Version = 16;

    /// <summary>Predefined resource type: application manifest.</summary>
    public const int Manifest = 24;

    /// <summary>The largest single resource payload the plugin will address; larger entries are ignored.</summary>
    public const int MaximumResourceBytes = 16 * 1024 * 1024;

    private const uint NameFlag = 0x8000_0000;
    private const uint DirectoryFlag = 0x8000_0000;
    private const int MaximumDirectoryEntries = 4096;

    private ExecutableResources(
        IReadOnlyList<ExecutableResource> iconGroups,
        IReadOnlyList<ExecutableResource> icons,
        ExecutableResource? version)
    {
        IconGroups = iconGroups;
        Icons = icons;
        VersionInfo = version;
    }

    /// <summary>The result for an image with no resource directory, or one that could not be read.</summary>
    public static ExecutableResources Empty { get; } = new([], [], null);

    /// <summary>Every <c>RT_GROUP_ICON</c> entry, ordered by resource id.</summary>
    public IReadOnlyList<ExecutableResource> IconGroups { get; }

    /// <summary>Every <c>RT_ICON</c> entry, ordered by resource id.</summary>
    public IReadOnlyList<ExecutableResource> Icons { get; }

    /// <summary>The version-information resource, preferring id 1, when the image carries one.</summary>
    public ExecutableResource? VersionInfo { get; }

    /// <summary>Reads the root, type, name and language levels of the resource directory.</summary>
    public static async Task<ExecutableResources> ReadAsync(
        ExecutableImage image,
        ExecutableSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(source);

        var groups = new List<ExecutableResource>();
        var icons = new List<ExecutableResource>();
        var versions = new List<ExecutableResource>();

        if (!image.HasResources || !image.TryGetFileOffset(image.ResourceRva, out var rootOffset))
        {
            return Empty;
        }

        var limit = Math.Min(source.Length, image.MaximumSectionEnd());
        foreach (var type in await ReadDirectoryAsync(source, rootOffset, limit, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (!type.IsDirectory || type.Id is not { } typeId ||
                typeId is not (Cursor or Icon or IconGroup or Version))
            {
                continue;
            }

            // Directory entries address their subtree relative to the resource root, not by RVA.
            if (!TryResolveTreeOffset(rootOffset, type.Target, limit, out var typeOffset))
            {
                continue;
            }

            foreach (var name in await ReadDirectoryAsync(source, typeOffset, limit, cancellationToken)
                         .ConfigureAwait(false))
            {
                // Only numeric names can be matched against the icon groups' image list; named resources and
                // named languages are skipped without reading anything.
                if (!name.IsDirectory || name.Id is not { } id || id < 0)
                {
                    continue;
                }

                if (!TryResolveTreeOffset(rootOffset, name.Target, limit, out var nameOffset))
                {
                    continue;
                }

                var language = SelectLanguage(
                    await ReadDirectoryAsync(source, nameOffset, limit, cancellationToken).ConfigureAwait(false));
                if (language is not { } leaf ||
                    !TryResolveTreeOffset(rootOffset, leaf.Target, limit, out var dataEntryOffset))
                {
                    continue;
                }

                var data = await ReadDataEntryAsync(source, dataEntryOffset, limit, cancellationToken)
                    .ConfigureAwait(false);
                if (data is not { } entry)
                {
                    continue;
                }

                if (!image.TryGetFileOffset(entry.Rva, out var dataOffset))
                {
                    continue;
                }

                var resource = new ExecutableResource(typeId, id, leaf.Id ?? 0, dataOffset, entry.Size);
                if (typeId == IconGroup)
                {
                    groups.Add(resource);
                }
                else if (typeId == Icon)
                {
                    icons.Add(resource);
                }
                else if (typeId == Version)
                {
                    versions.Add(resource);
                }
            }
        }

        groups.Sort(static (left, right) => left.Name.CompareTo(right.Name));
        icons.Sort(static (left, right) => left.Name.CompareTo(right.Name));

        ExecutableResource? version = null;
        if (versions.Count > 0)
        {
            version = versions.Find(candidate => candidate.Name == 1);
            version ??= versions[0];
        }

        return new ExecutableResources(groups, icons, version);
    }

    /// <summary>Neutral language first, then the lowest numeric id; named languages never reach here.</summary>
    private static ExecutableDirectoryEntry? SelectLanguage(IReadOnlyList<ExecutableDirectoryEntry> entries)
    {
        ExecutableDirectoryEntry? best = null;
        foreach (var entry in entries)
        {
            if (entry.IsDirectory || entry.Id is not { } id || id < 0)
            {
                continue;
            }

            if (best is null || Rank(id) < Rank(best.Value.Id!.Value))
            {
                best = entry;
            }
        }

        return best;
    }

    private static int Rank(int id) => id == 0 ? int.MinValue : id;

    /// <summary>Translates a resource-tree target, which is relative to the root, into a file offset.</summary>
    private static bool TryResolveTreeOffset(long rootOffset, uint target, long limit, out long offset)
    {
        offset = rootOffset + target;
        return rootOffset >= 0 && offset <= limit;
    }

    /// <summary>Reads one directory node: its header plus the fixed-size entries it declares.</summary>
    private static async Task<IReadOnlyList<ExecutableDirectoryEntry>> ReadDirectoryAsync(
        ExecutableSource source,
        long offset,
        long limit,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > limit || limit - offset < 16)
        {
            return [];
        }

        var header = await source.ReadAsync(offset, 16, cancellationToken).ConfigureAwait(false);
        var named = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12, 2));
        var ids = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14, 2));
        long count = named + ids;
        if (count == 0)
        {
            return [];
        }

        if (count > MaximumDirectoryEntries || limit - offset - 16 < count * 8)
        {
            throw new ExecutableReadException("a resource directory does not fit in the image");
        }

        var bytes = await source.ReadAsync(offset + 16, (int)count * 8, cancellationToken).ConfigureAwait(false);
        var entries = new List<ExecutableDirectoryEntry>((int)count);
        for (var index = 0; index < count; index++)
        {
            var name = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 8, 4));
            var target = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 8 + 4, 4));
            entries.Add(new ExecutableDirectoryEntry(
                (name & NameFlag) == 0 ? (int)(name & ~NameFlag) : null,
                (target & DirectoryFlag) != 0,
                target & ~DirectoryFlag));
        }

        return entries;
    }

    /// <summary>Reads one leaf data entry: the payload address and its declared size.</summary>
    private static async Task<(uint Rva, int Size)?> ReadDataEntryAsync(
        ExecutableSource source,
        long offset,
        long limit,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > limit || limit - offset < 16)
        {
            return null;
        }

        var data = await source.ReadAsync(offset, 16, cancellationToken).ConfigureAwait(false);
        var rva = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        return size == 0 || size > MaximumResourceBytes ? null : (rva, (int)size);
    }

    /// <summary>One raw directory sub-entry.</summary>
    internal readonly record struct ExecutableDirectoryEntry(int? Id, bool IsDirectory, uint Target);
}
