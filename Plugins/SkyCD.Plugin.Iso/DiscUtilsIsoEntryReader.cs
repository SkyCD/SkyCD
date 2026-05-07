using System.Collections.Generic;
using System.IO;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Iso;

public sealed class DiscUtilsIsoEntryReader : IIsoEntryReader
{
    public IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source)
    {
        using var reader = new CDReader(source, joliet: true);
        var entries = new List<IsoEntryInfo>();
        TraverseDirectory(reader, path: string.Empty, entries);
        return entries;
    }

    private static void TraverseDirectory(CDReader reader, string path, List<IsoEntryInfo> entries)
    {
        foreach (var directory in reader.GetDirectories(path))
        {
            var normalized = directory.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(normalized, IsDirectory: true, SizeBytes: 0, ModifiedUtc: null));
            TraverseDirectory(reader, normalized, entries);
        }

        foreach (var file in reader.GetFiles(path))
        {
            var normalized = file.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(
                normalized,
                IsDirectory: false,
                SizeBytes: reader.GetFileLength(normalized),
                ModifiedUtc: reader.GetLastWriteTimeUtc(normalized)));
        }
    }
}
