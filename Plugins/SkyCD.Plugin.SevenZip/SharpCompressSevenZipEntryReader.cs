using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives.SevenZip;

namespace SkyCD.Plugin.SevenZip;

public sealed class SharpCompressSevenZipEntryReader : ISevenZipEntryReader
{
    public IReadOnlyCollection<SevenZipEntryInfo> ReadEntries(Stream source)
    {
        using var archive = SevenZipArchive.Open(source);
        return archive.Entries
            .Where(entry => !entry.IsEncrypted)
            .Select(entry => new SevenZipEntryInfo(
                entry.Key ?? string.Empty,
                entry.IsDirectory,
                entry.Size,
                entry.LastModifiedTime is DateTime value
                    ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    : null))
            .ToList();
    }
}
