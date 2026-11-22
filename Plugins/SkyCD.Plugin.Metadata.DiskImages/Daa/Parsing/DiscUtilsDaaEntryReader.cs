using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Metadata.DiskImages.Daa.Parsing;

public sealed record DaaEntryInfo(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime? ModifiedUtc,
    string? LookupPath = null);

public interface IDaaEntryReader
{
    IReadOnlyCollection<DaaEntryInfo> ReadEntries(Stream image);
}

public sealed class DiscUtilsDaaEntryReader : IDaaEntryReader
{
    private const int MaxDepth = 64;

    public IReadOnlyCollection<DaaEntryInfo> ReadEntries(Stream image)
    {
        using var reader = new CDReader(image, joliet: true, hideVersions: true);
        var entries = new List<DaaEntryInfo>();
        TraverseDirectory(reader, path: string.Empty, depth: 0, entries);
        return entries;
    }

    private static void TraverseDirectory(CDReader reader, string path, int depth, List<DaaEntryInfo> entries)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        var lookupPath = path.TrimStart('/', '\\');

        foreach (var directory in reader.GetDirectories(lookupPath))
        {
            var normalized = directory.Replace('\\', '/');
            entries.Add(new DaaEntryInfo(normalized, IsDirectory: true, SizeBytes: 0, ModifiedUtc: null,
                LookupPath: directory.TrimStart('/', '\\')));
            TraverseDirectory(reader, directory, depth + 1, entries);
        }

        foreach (var file in reader.GetFiles(lookupPath))
        {
            var normalized = file.Replace('\\', '/');
            entries.Add(new DaaEntryInfo(
                normalized,
                IsDirectory: false,
                SizeBytes: reader.GetFileLength(file),
                ModifiedUtc: reader.GetLastWriteTimeUtc(file),
                LookupPath: file.TrimStart('/', '\\')));
        }
    }
}
