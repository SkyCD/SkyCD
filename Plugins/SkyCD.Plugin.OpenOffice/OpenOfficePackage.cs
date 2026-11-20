using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using SkyCD.Plugin.OpenOffice.Exceptions;

namespace SkyCD.Plugin.OpenOffice;

/// <summary>
/// Read-only view of an OpenDocument or OpenOffice.org package: a ZIP whose members include a declared MIME
/// type, the document metadata and a manifest.
/// </summary>
/// <remarks>
/// Only three small members are read, each into a buffer bounded on the bytes actually decompressed, so a
/// package that declares a small member but expands far beyond the bound is refused rather than materialised.
/// Other members, including embedded objects and images, are never opened.
/// </remarks>
internal sealed class OpenOfficePackage : IDisposable
{
    private const string MimeTypeEntryName = "mimetype";
    private const string MetadataEntryName = "meta.xml";
    private const string ManifestEntryName = "META-INF/manifest.xml";
    private const int ChunkBytes = 64 * 1024;

    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries;

    private OpenOfficePackage(ZipArchive archive)
    {
        this.archive = archive;
        entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith('/'))
            {
                entries.TryAdd(entry.FullName, entry);
            }
        }
    }

    public static OpenOfficePackage Open(byte[] data)
    {
        try
        {
            return new OpenOfficePackage(
                new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read));
        }
        catch (InvalidDataException exception)
        {
            throw new OpenOfficeDecodeException("the package is not a valid ZIP container", exception);
        }
    }

    /// <summary>The declared package MIME type, or <see langword="null"/> when it is absent or unreadable.</summary>
    public string? TryReadContentType(int maxBytes)
    {
        var bytes = TryReadEntry(MimeTypeEntryName, maxBytes);
        if (bytes is null)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(bytes).Trim('\uFEFF', ' ', '\t', '\r', '\n');
        return text.Length == 0 ? null : text;
    }

    /// <summary>The <c>meta.xml</c> bytes, or <see langword="null"/> when the member is absent, empty or over the bound.</summary>
    public byte[]? TryReadMetadata(int maxBytes) => TryReadEntry(MetadataEntryName, maxBytes);

    /// <summary>
    /// True when the package manifest declares encrypted content. The manifest itself is never encrypted, so
    /// it is the reliable signal that the readable members need a credential the host does not hold.
    /// </summary>
    public bool DeclaresEncryption(int maxBytes)
    {
        var manifest = TryReadEntry(ManifestEntryName, maxBytes);
        if (manifest is null)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(manifest, writable: false);
            using var reader = XmlReader.Create(stream, OpenOfficeXml.CreateSettings(maxBytes));
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    OpenOfficeNamespaces.IsManifest(reader.NamespaceURI, reader.LocalName))
                {
                    return true;
                }
            }
        }
        catch (XmlException)
        {
            // A manifest that cannot be read declares nothing this plugin can honour; the metadata entry
            // itself is still attempted, so an ordinary package with a damaged manifest keeps its metadata.
        }

        return false;
    }

    public void Dispose() => archive.Dispose();

    /// <summary>
    /// Reads a member into memory, refusing it when the declared size is over the bound and again when the
    /// decompressed bytes actually produced exceed it. A short, damaged or unsupported member reads as
    /// absent so that the container facts already known are still published.
    /// </summary>
    private byte[]? TryReadEntry(string name, int maxBytes)
    {
        if (!entries.TryGetValue(name, out var entry) || entry.Length <= 0 || entry.Length > maxBytes)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
            var chunk = new byte[Math.Min(ChunkBytes, maxBytes)];
            var total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
