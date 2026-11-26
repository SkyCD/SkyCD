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
/// The accepted families (text, spreadsheet, presentation, graphics) are read through <see cref="OdfDocument"/>,
/// which enforces bounded access and reuses the existing metadata parser for <c>meta.xml</c>. Every other
/// family (templates, master/web, chart, image, database, formula and every legacy <c>.sx*</c> package) falls
/// back to the bounded member reader so the values it publishes stay unchanged. The plugin holds bytes through
/// the host's bounded source, never a path, so nothing in a package can make it open another file. No
/// credential is ever requested, guessed, stored or used.
/// </remarks>
internal sealed class OpenOfficePackage : IDisposable
{
    private const string MimeTypeEntryName = "mimetype";
    private const string MetadataEntryName = "meta.xml";
    private const string ManifestEntryName = "META-INF/manifest.xml";
    private const string ThumbnailEntryName = "Thumbnails/thumbnail.png";
    private const int ChunkBytes = 64 * 1024;

    private readonly OdfDocument? officeImoDocument;
    private readonly bool isOfficeImo;
    private byte[]? legacyData;
    private ZipArchive? legacyArchive;
    private Dictionary<string, ZipArchiveEntry>? legacyEntries;

    private OpenOfficePackage(OdfDocument officeImoDocument, bool isOfficeImo)
    {
        this.officeImoDocument = officeImoDocument;
        this.isOfficeImo = isOfficeImo;
    }

    private OpenOfficePackage(byte[] data, bool isOfficeImo)
    {
        this.isOfficeImo = isOfficeImo;
        this.legacyData = data;
        if (!isOfficeImo)
        {
            BuildLegacyEntries();
        }
    }

    public static OpenOfficePackage Open(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var document = OdfDocument.Load(stream, new OdfLoadOptions
            {
                MaxPackageBytes = int.MaxValue,
                MaxEntries = int.MaxValue,
                MaxDepth = int.MaxValue,
                MaxEntryUncompressedBytes = int.MaxValue,
                MaxTotalUncompressedBytes = int.MaxValue,
                MaxXmlCharacters = int.MaxValue,
                MaxXmlDepth = int.MaxValue
            });
            return new OpenOfficePackage(document, isOfficeImo: true);
        }
        catch (OdfEncryptedPackageException exception)
        {
            throw new OpenOfficeAuthenticationRequiredException("the package is encrypted", exception);
        }
        catch (Exception exception) when (exception is not OpenOfficeDecodeException and not OpenOfficeAuthenticationRequiredException)
        {
            // The OfficeIMO reader accepts only the text, spreadsheet, presentation and graphics media types, so
            // families it rejects, or a package it cannot parse (for example, a legacy package whose manifest
            // carries an encryption marker), fall back to the existing bounded member reader.
            return new OpenOfficePackage(data, isOfficeImo: false);
        }
    }

    public string? TryReadContentType(int maxBytes)
    {
        if (isOfficeImo)
        {
            var bytes = officeImoDocument!.GetPackageEntryBytes(MimeTypeEntryName, maxBytes);
            if (bytes is null)
            {
                return null;
            }

            var text = Encoding.ASCII.GetString(bytes).Trim('\uFEFF', ' ', '\t', '\r', '\n');
            return text.Length == 0 ? null : text;
        }

        return legacyTryReadContentType(maxBytes);
    }

    public byte[]? TryReadMetadata(int maxBytes)
    {
        if (isOfficeImo)
        {
            return officeImoDocument!.GetPackageEntryBytes(MetadataEntryName, maxBytes);
        }

        return legacyTryReadMetadata(maxBytes);
    }

    public byte[]? TryReadThumbnail(int maxBytes, CancellationToken cancellationToken)
    {
        if (isOfficeImo)
        {
            return officeImoDocument!.GetPackageEntryBytes(ThumbnailEntryName, maxBytes);
        }

        return legacyTryReadThumbnail(maxBytes, cancellationToken);
    }

    public bool DeclaresEncryption(int maxBytes)
    {
        if (isOfficeImo)
        {
            return officeImoDocument!.Security.SourceIsEncrypted;
        }

        return legacyDeclaresEncryption(maxBytes);
    }

    public void Dispose()
    {
        if (isOfficeImo)
        {
            officeImoDocument!.Dispose();
        }

        legacyArchive?.Dispose();
    }

    private void BuildLegacyEntries()
    {
        using var stream = new MemoryStream(legacyData!, writable: false);
        legacyArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        legacyEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in legacyArchive.Entries)
        {
            if (!entry.FullName.EndsWith('/'))
            {
                legacyEntries.TryAdd(entry.FullName, entry);
            }
        }
    }

    private string? legacyTryReadContentType(int maxBytes)
    {
        if (!legacyEntries!.TryGetValue(MimeTypeEntryName, out var entry) || entry.Length <= 0 || entry.Length > maxBytes)
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
            while ((read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, maxBytes - total))) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            var text = Encoding.ASCII.GetString(buffer.ToArray()).Trim('\uFEFF', ' ', '\t', '\r', '\n');
            return text.Length == 0 ? null : text;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private byte[]? legacyTryReadMetadata(int maxBytes) => legacyTryReadEntry(MetadataEntryName, maxBytes);

    private byte[]? legacyTryReadThumbnail(int maxBytes, CancellationToken cancellationToken) =>
        legacyTryReadEntry(ThumbnailEntryName, maxBytes, cancellationToken);

    private byte[]? legacyTryReadEntry(string name, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (!legacyEntries!.TryGetValue(name, out var entry) || entry.Length <= 0 || entry.Length > maxBytes)
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private bool legacyDeclaresEncryption(int maxBytes)
    {
        var manifest = legacyTryReadEntry(ManifestEntryName, maxBytes);
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
}
