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
        var lookupPath = NormalizeLookupPath(path);

        foreach (var directory in reader.GetDirectories(lookupPath))
        {
            var normalized = directory.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(normalized, IsDirectory: true, SizeBytes: 0, ModifiedUtc: null));
            TraverseDirectory(reader, normalized, entries);
        }

        foreach (var file in reader.GetFiles(lookupPath))
        {
            var normalized = file.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(
                normalized,
                IsDirectory: false,
                SizeBytes: reader.GetFileLength(file),
                ModifiedUtc: reader.GetLastWriteTimeUtc(file)));
        }
    }

    private static string NormalizeLookupPath(string path)
    {
        return path.TrimStart('/', '\\');
    }
}
