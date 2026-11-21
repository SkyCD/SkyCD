using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.EBook.Exceptions;
using SkyCD.Plugin.EBook.Models;

namespace SkyCD.Plugin.EBook.Parsing;

/// <summary>
/// Reads the package document of an EPUB (or an Apple iBook, which is the same packaging) and, on request, the
/// cover image it names.
/// </summary>
/// <remarks>
/// The archive is read through <see cref="MediaSourceStream"/>, so a large book is never buffered whole and
/// every read is charged to the host's budget. Only the container, package, encryption and cover entries are
/// decompressed, each with its own declared and actual size bound, and nothing is written to disk, so an entry
/// name has nowhere to escape to. An unreadable container or package raises
/// <see cref="EBookDecodeException"/>; an unreadable optional part simply yields no value.
/// </remarks>
internal static class EpubReader
{
    private const string ContainerPath = "META-INF/container.xml";
    private const string EncryptionPath = "META-INF/encryption.xml";

    private static readonly string[] FontExtensions = [".otf", ".ttf", ".ttc", ".woff", ".woff2"];

    public static async Task<EBookDocument> ReadAsync(
        IMediaSource source,
        string extension,
        EBookPluginOptions options,
        bool readCover,
        CancellationToken cancellationToken)
    {
        using var stream = new MediaSourceStream(source, cancellationToken);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new EBookDecodeException("the EPUB archive is damaged", exception);
        }

        using (archive)
        {
            if (archive.Entries.Count > options.MaxArchiveEntries)
            {
                throw new EBookDecodeException("the EPUB archive declares too many entries");
            }

            var packagePath = await FindPackagePathAsync(archive, options, cancellationToken).ConfigureAwait(false)
                              ?? throw new EBookDecodeException("the EPUB package document is missing");
            var packageEntry = FindEntry(archive, packagePath)
                               ?? throw new EBookDecodeException("the EPUB package document is missing");

            EBookDocument document;
            try
            {
                var packageBytes = await ReadEntryAsync(packageEntry, options.MaxMetadataPartBytes, cancellationToken)
                    .ConfigureAwait(false);
                var packageXml = packageBytes.Length == 0
                    ? null
                    : EBookXml.TryLoad(packageBytes, options.MaxMetadataPartBytes);
                var package = packageXml is null
                    ? null
                    : EpubPackage.Parse(packageXml, options.MaxFieldCharacters);
                if (package is null)
                {
                    throw new EBookDecodeException("the EPUB package document is not a readable package");
                }

                var cover = readCover
                    ? await ReadCoverAsync(archive, package, packagePath, options, cancellationToken).ConfigureAwait(false)
                    : null;
                var isProtected = await IsProtectedAsync(archive, options, cancellationToken).ConfigureAwait(false);

                document = new EBookDocument
                {
                    Container = "zip",
                    // An Apple iBook is the same package sold under a different extension.
                    Format = string.Equals(extension, ".ibook", StringComparison.OrdinalIgnoreCase) ? "ibook" : "epub",
                    Title = package.Title,
                    Authors = package.Authors,
                    Publisher = package.Publisher,
                    Language = package.Language,
                    Subject = package.Subject,
                    Description = package.Description,
                    Identifier = package.Identifier,
                    PublishedAt = package.PublishedAt,
                    PublishedYear = package.PublishedAt?.Year,
                    IsProtected = isProtected,
                    Cover = cover
                };
            }
            catch (InvalidDataException exception)
            {
                throw new EBookDecodeException("the EPUB archive is damaged", exception);
            }
            catch (NotSupportedException exception)
            {
                throw new EBookDecodeException("the EPUB archive uses an unsupported compression method", exception);
            }

            return document;
        }
    }

    /// <summary>
    /// Locates the package document through <c>META-INF/container.xml</c>. A container that is missing or
    /// unreadable falls back to the shallowest <c>.opf</c> entry, which is what real packages carry anyway.
    /// </summary>
    private static async Task<string?> FindPackagePathAsync(
        ZipArchive archive,
        EBookPluginOptions options,
        CancellationToken cancellationToken)
    {
        var containerEntry = FindEntry(archive, ContainerPath);
        if (containerEntry is not null)
        {
            var bytes = await ReadEntryAsync(containerEntry, options.MaxMetadataPartBytes, cancellationToken)
                .ConfigureAwait(false);
            var document = bytes.Length == 0 ? null : EBookXml.TryLoad(bytes, options.MaxMetadataPartBytes);
            var path = document?.Descendants()
                .Where(element => element.Name.LocalName.Equals("rootfile", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.LocalName.Equals("full-path", StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (path is not null)
            {
                return path;
            }
        }

        return archive.Entries
            .Where(entry => entry.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Count(character => character == '/'))
            .ThenBy(entry => entry.FullName, StringComparer.Ordinal)
            .Select(entry => entry.FullName)
            .FirstOrDefault();
    }

    private static async Task<byte[]?> ReadCoverAsync(
        ZipArchive archive,
        EpubPackage package,
        string packagePath,
        EBookPluginOptions options,
        CancellationToken cancellationToken)
    {
        var href = package.FindCoverHref();
        if (href is null)
        {
            return null;
        }

        string path;
        try
        {
            path = ResolveHref(packagePath, href);
        }
        catch (EBookDecodeException)
        {
            // A cover that points outside the archive costs the cover only; the metadata stands.
            return null;
        }

        var entry = FindEntry(archive, path);
        if (entry is null)
        {
            return null;
        }

        var data = await ReadEntryAsync(entry, options.MaxCoverBytes, cancellationToken).ConfigureAwait(false);
        return EBookSignature.IsImage(data) ? data : null;
    }

    /// <summary>
    /// Reports encryption only when something other than a font is encrypted. EPUB font obfuscation is
    /// declared through the same <c>encryption.xml</c>, and treating it as DRM would mislabel ordinary books.
    /// </summary>
    private static async Task<bool> IsProtectedAsync(
        ZipArchive archive,
        EBookPluginOptions options,
        CancellationToken cancellationToken)
    {
        var entry = FindEntry(archive, EncryptionPath);
        if (entry is null)
        {
            return false;
        }

        var bytes = await ReadEntryAsync(entry, options.MaxMetadataPartBytes, cancellationToken).ConfigureAwait(false);
        var document = bytes.Length == 0 ? null : EBookXml.TryLoad(bytes, options.MaxMetadataPartBytes);
        if (document is null)
        {
            return false;
        }

        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("CipherReference", StringComparison.OrdinalIgnoreCase)))
        {
            var uri = reference.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("URI", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (uri is null)
            {
                continue;
            }

            var extension = Path.GetExtension(uri);
            if (!FontExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a manifest href against the directory of the package document and normalizes it.</summary>
    internal static string ResolveHref(string packagePath, string href)
    {
        var path = href.Split('#')[0].Split('?')[0];
        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            // A malformed escape is used as-is; entry lookup is exact and will simply fail.
        }

        var directory = packagePath.LastIndexOf('/') is var slash && slash >= 0 ? packagePath[..slash] : string.Empty;
        var combined = path.StartsWith('/') ? path[1..] : (directory.Length == 0 ? path : directory + "/" + path);

        var segments = new List<string>();
        foreach (var segment in combined.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new EBookDecodeException("an EPUB href escapes the archive root");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path) =>
        archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/').TrimStart('/'), path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads one entry into memory, refusing entries whose declared or actual size exceeds the bound. An
    /// oversized optional part reads as empty, which callers treat as "not present".
    /// </summary>
    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length == 0 || entry.Length > maxBytes)
        {
            return [];
        }

        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maxBytes)
            {
                // The declared size lied; the decompressed part is not used at all.
                return [];
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
