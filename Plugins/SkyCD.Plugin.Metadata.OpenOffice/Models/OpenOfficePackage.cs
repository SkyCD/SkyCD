using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;
using OfficeIMO.OpenDocument;
using SkyCD.Plugin.Metadata.OpenOffice.Exceptions;
using SkyCD.Plugin.Metadata.OpenOffice.Parsing;

namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>
/// Read-only view of an OpenDocument or OpenOffice.org package: a ZIP whose members include a declared MIME
/// type, the document metadata and a manifest.
/// </summary>
/// <remarks>
/// The package is opened as a ZIP first, because only the ZIP says which members the document really carries:
/// the OfficeIMO reader publishes a metadata part of its own for a package that has none. The OfficeIMO reader
/// then validates the container and serves the member bytes for the families it accepts. The families it
/// rejects - templates, master and web documents, charts, images, databases, formulas and the legacy
/// OpenOffice.org packages - and the packages it cannot parse are read from the ZIP members instead, each into
/// a buffer bounded on the bytes actually decompressed, so a package that declares a small member but expands
/// far beyond the bound is refused rather than materialised. Other members, including embedded objects and
/// images, are never opened.
/// </remarks>
internal sealed class OpenOfficePackage : IDisposable
{
    private const string MimeTypeEntryName = "mimetype";
    private const string MetadataEntryName = "meta.xml";
    private const string ManifestEntryName = "META-INF/manifest.xml";
    private const string ThumbnailEntryName = "Thumbnails/thumbnail.png";
    private const int ChunkBytes = 64 * 1024;
    private const int MaxPackageEntries = 10_000;
    private const int MaxPackageDepth = 32;
    private const int MaxXmlDepth = 256;

    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries;
    private readonly OdfDocument? document;

    private OpenOfficePackage(ZipArchive archive, OdfDocument? document)
    {
        this.archive = archive;
        this.document = document;
        entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith('/'))
            {
                entries.TryAdd(entry.FullName, entry);
            }
        }
    }

    public static OpenOfficePackage Open(byte[] data, OpenOfficePluginOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            throw new OpenOfficeDecodeException("the package is not a valid ZIP container", exception);
        }

        try
        {
            return new OpenOfficePackage(archive, TryLoadDocument(data, options));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>Which reader served this package.</summary>
    public OpenOfficePackageSource Source =>
        document is null ? OpenOfficePackageSource.ZipMembers : OpenOfficePackageSource.OfficeImo;

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
    /// The <c>Thumbnails/thumbnail.png</c> bytes, or <see langword="null"/> when the member is absent, empty,
    /// over the bound or damaged.
    /// </summary>
    public byte[]? TryReadThumbnail(int maxBytes, CancellationToken cancellationToken) =>
        TryReadEntry(ThumbnailEntryName, maxBytes, cancellationToken);

    /// <summary>
    /// True when the package manifest declares encrypted content. The manifest itself is never encrypted, so
    /// it is the reliable signal that the readable members need a credential the host does not hold.
    /// </summary>
    public bool DeclaresEncryption(int maxBytes)
    {
        if (document is not null)
        {
            // A package the reader accepted carries no encrypted member.
            return document.Security.SourceIsEncrypted;
        }

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

    public void Dispose()
    {
        document?.Dispose();
        archive.Dispose();
    }

    /// <summary>
    /// Reads a member into memory, refusing it when the declared size is over the bound and again when the
    /// decompressed bytes actually produced exceed it. A short, damaged or unsupported member reads as
    /// absent so that the container facts already known are still published.
    /// </summary>
    private byte[]? TryReadEntry(string name, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (!entries.TryGetValue(name, out var entry) || entry.Length <= 0 || entry.Length > maxBytes)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return document is not null
            ? TryReadThroughDocument(document, entry.FullName, maxBytes)
            : TryReadThroughArchive(entry, maxBytes, cancellationToken);
    }

    /// <summary>Reads a member through the OfficeIMO reader, which refuses a member above the bound.</summary>
    private static byte[]? TryReadThroughDocument(OdfDocument document, string name, int maxBytes)
    {
        try
        {
            return document.GetPackageEntryBytes(name, maxBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            // The member is missing or larger than the bound, so it reads as absent.
            return null;
        }
    }

    /// <summary>Reads a member from the ZIP, refusing it again when the bytes produced exceed the bound.</summary>
    private static byte[]? TryReadThroughArchive(
        ZipArchiveEntry entry,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
            var chunk = new byte[Math.Min(ChunkBytes, maxBytes)];
            var total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, maxBytes - total))) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Loads the package with the OfficeIMO reader, which validates the container and bounds every member it
    /// opens. A package the reader does not accept is read from its ZIP members instead, so the families it
    /// rejects keep the metadata they have always published.
    /// </summary>
    private static OdfDocument? TryLoadDocument(byte[] data, OpenOfficePluginOptions options)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            return OdfDocument.Load(stream, BuildLoadOptions(options));
        }
        catch (OdfEncryptedPackageException)
        {
            // A protected package still carries its manifest, which declares the encryption, and the
            // metadata the manifest leaves readable. Both are read from the ZIP members; no credential is
            // requested, guessed or stored.
            return null;
        }
        catch (InvalidDataException)
        {
            // The declared media type is one the reader does not accept, or the container does not hold the
            // parts it requires.
            return null;
        }
        catch (XmlException)
        {
            // A damaged XML part leaves the package unreadable to the reader.
            return null;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException)
        {
            // The container holds something the reader cannot open.
            return null;
        }
    }

    /// <summary>
    /// Bounds for the reader, taken from the plugin's own source budget so that a package can never expand
    /// further than the source the host already accepted.
    /// </summary>
    private static OdfLoadOptions BuildLoadOptions(OpenOfficePluginOptions options) => new()
    {
        MaxPackageBytes = options.MaxSourceBytes,
        MaxEntries = MaxPackageEntries,
        MaxDepth = MaxPackageDepth,
        MaxEntryUncompressedBytes = options.MaxSourceBytes,
        MaxTotalUncompressedBytes = options.MaxSourceBytes,
        MaxXmlCharacters = options.MaxSourceBytes,
        MaxXmlDepth = MaxXmlDepth
    };
}
