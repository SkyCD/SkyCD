using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SkyCD.Plugin.IWork.Exceptions;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Read-only view of a modern iWork package: a ZIP whose index lists the document components.
/// </summary>
/// <remarks>
/// Two layouts are accepted because both are shipped by Apple. A flat package stores the component archive
/// directly as <c>Index/*.iwa</c>; a bundle stored as one file keeps that archive as a nested <c>Index.zip</c>.
/// Only entry names and a few bounded members are read, so a package the plugin cannot fully parse still yields
/// its inventory instead of failing.
/// </remarks>
internal sealed class IWorkPackage : IDisposable
{
    private const string DocumentIndexName = "Index/Document.iwa";
    private const string NestedIndexName = "Index.zip";
    private const string PasswordVerifierName = ".iwpv2";

    private static readonly string[] LegacyIndexNames =
    [
        "index.xml",
        "index.apxl",
        "index.apxl.gz",
        "Index/index.xml",
        "Index/index.apxl",
        "Index/index.apxl.gz"
    ];

    private readonly ZipArchive outer;
    private readonly ZipArchive? nested;
    private readonly Dictionary<string, ZipArchiveEntry> outerEntries;
    private readonly Dictionary<string, ZipArchiveEntry> indexEntries;

    private IWorkPackage(
        ZipArchive outer,
        Dictionary<string, ZipArchiveEntry> outerEntries,
        ZipArchive? nested,
        Dictionary<string, ZipArchiveEntry> indexEntries)
    {
        this.outer = outer;
        this.outerEntries = outerEntries;
        this.nested = nested;
        this.indexEntries = indexEntries;
        EntryNames = outerEntries.Keys.ToArray();
        IndexEntryNames = indexEntries.Keys.ToArray();
    }

    /// <summary>Entries at the package root, such as metadata members, previews and data assets.</summary>
    public IReadOnlyList<string> EntryNames { get; }

    /// <summary>Entries of the component archive, including the document index and any slide or table streams.</summary>
    public IReadOnlyList<string> IndexEntryNames { get; }

    /// <summary>
    /// True when the package carries an iWork password verifier. The verifier and the two plain metadata
    /// members stay readable; everything else is encrypted and is left untouched.
    /// </summary>
    public bool IsPasswordProtected => outerEntries.ContainsKey(PasswordVerifierName);

    public static IWorkPackage Open(byte[] data, IWorkPluginOptions options)
    {
        var outer = OpenArchive(data, "ZIP container");
        var outerEntries = CollectEntries(outer);

        if (outerEntries.ContainsKey(DocumentIndexName))
        {
            return new IWorkPackage(outer, outerEntries, null, outerEntries);
        }

        if (outerEntries.TryGetValue(NestedIndexName, out var nestedEntry))
        {
            try
            {
                var nestedBytes = ReadEntry(nestedEntry, options.MaxIndexBytes)
                                  ?? throw new IWorkDecodeException(
                                      "the nested index is larger than the configured bound or unreadable");
                var nested = OpenArchive(nestedBytes, "nested index");
                var nestedEntries = CollectEntries(nested);
                if (!nestedEntries.ContainsKey(DocumentIndexName))
                {
                    nested.Dispose();
                    throw BuildMissingIndexException(nestedEntries.Keys);
                }

                return new IWorkPackage(outer, outerEntries, nested, nestedEntries);
            }
            catch
            {
                outer.Dispose();
                throw;
            }
        }

        outer.Dispose();
        throw BuildMissingIndexException(outerEntries.Keys);
    }

    public byte[]? TryReadEntry(string name, int maxBytes) =>
        outerEntries.TryGetValue(name, out var entry) ? ReadEntry(entry, maxBytes) : null;

    public byte[]? TryReadEntryPrefix(string name, int maxBytes) =>
        outerEntries.TryGetValue(name, out var entry) ? ReadEntryPrefix(entry, maxBytes) : null;

    public void Dispose()
    {
        nested?.Dispose();
        outer.Dispose();
    }

    private static ZipArchive OpenArchive(byte[] data, string description)
    {
        try
        {
            return new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            throw new IWorkDecodeException($"the {description} is not a valid ZIP container", exception);
        }
    }

    private static Dictionary<string, ZipArchiveEntry> CollectEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            entries.TryAdd(entry.FullName, entry);
        }

        return entries;
    }

    private static byte[]? ReadEntry(ZipArchiveEntry entry, int maxBytes)
    {
        if (entry.Length <= 0 || entry.Length > maxBytes)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var buffer = new byte[(int)entry.Length];
            stream.ReadExactly(buffer.AsSpan());
            return buffer;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static byte[]? ReadEntryPrefix(ZipArchiveEntry entry, int maxBytes)
    {
        if (entry.Length <= 0)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var buffer = new byte[maxBytes];
            var total = 0;
            while (total < maxBytes)
            {
                var read = stream.Read(buffer, total, maxBytes - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total == 0 ? null : buffer[..total];
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static Exception BuildMissingIndexException(IEnumerable<string> names)
    {
        var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        if (LegacyIndexNames.Any(present.Contains))
        {
            return new IWorkUnsupportedFormatException("legacy XML iWork packages are not supported");
        }

        return new IWorkDecodeException($"the package does not contain {DocumentIndexName}");
    }
}
