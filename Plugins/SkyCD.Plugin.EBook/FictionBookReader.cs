using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.EBook.Exceptions;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Reads a FictionBook 2 XML document: the <c>description</c> block and, on request, the cover image stored as
/// a base64 <c>binary</c> element.
/// </summary>
/// <remarks>
/// FB2 is XML, so the document has to be read whole; the source bound applies before any byte is read and the
/// reader refuses DTDs and external resolution. Only the binary the cover references is decoded, and only
/// after a base64 length bound check, so an embedded gallery is never materialized.
/// </remarks>
internal static class FictionBookReader
{
    private const int ChunkBytes = 256 * 1024;

    public static async Task<EBookDocument> ReadAsync(
        IMediaSource source,
        EBookPluginOptions options,
        bool readCover,
        CancellationToken cancellationToken)
    {
        var data = await ReadAllAsync(source, options.MaxSourceBytes, cancellationToken).ConfigureAwait(false);
        var document = EBookXml.TryLoad(data, options.MaxSourceBytes)
                       ?? throw new EBookDecodeException("the FictionBook document is not well-formed XML");
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "FictionBook", StringComparison.OrdinalIgnoreCase))
        {
            throw new EBookDecodeException("the document is not a FictionBook");
        }

        var description = Child(root, "description");
        var titleInfo = Child(description, "title-info");
        var publishInfo = Child(description, "publish-info");
        var documentInfo = Child(description, "document-info");
        var limit = options.MaxFieldCharacters;

        var authors = new List<string>();
        if (titleInfo is not null)
        {
            foreach (var author in titleInfo.Elements()
                         .Where(element => element.Name.LocalName.Equals("author", StringComparison.OrdinalIgnoreCase)))
            {
                var name = EBookText.Normalize(string.Join(' ', new[]
                    {
                        Text(Child(author, "first-name")),
                        Text(Child(author, "middle-name")),
                        Text(Child(author, "last-name"))
                    }.Where(part => !string.IsNullOrWhiteSpace(part))), limit)
                    ?? EBookText.Normalize(Text(Child(author, "nickname")), limit);
                if (name is not null)
                {
                    authors.Add(name);
                }
            }
        }

        var coverHref = titleInfo is null
            ? null
            : Child(titleInfo, "coverpage")?
                .Elements()
                .Where(element => element.Name.LocalName.Equals("image", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var cover = readCover ? ReadCover(root, coverHref, options) : null;

        return new EBookDocument
        {
            Container = "xml",
            Format = "fb2",
            Title = EBookText.Normalize(Text(Child(titleInfo, "book-title")), limit),
            Authors = authors,
            Publisher = EBookText.Normalize(Text(Child(publishInfo, "publisher")), limit),
            Language = EBookText.Normalize(Text(Child(titleInfo, "lang")), limit),
            Subject = EBookText.Normalize(Text(Child(titleInfo, "genre")), limit),
            Description = EBookText.Normalize(Text(Child(titleInfo, "annotation")), limit),
            Identifier = EBookText.Normalize(Text(Child(publishInfo, "isbn")), limit)
                         ?? EBookText.Normalize(Text(Child(documentInfo, "id")), limit),
            PublishedAt = EBookText.ParseTimestamp(Text(Child(publishInfo, "year"))),
            PublishedYear = EBookText.ParseYear(Text(Child(publishInfo, "year"))),
            Cover = cover
        };
    }

    /// <summary>
    /// Finds the <c>binary</c> whose id the coverpage references and decodes it. A missing, oversized or
    /// undecodable binary simply yields no cover.
    /// </summary>
    private static byte[]? ReadCover(XElement root, string? href, EBookPluginOptions options)
    {
        if (href is null)
        {
            return null;
        }

        var id = href.TrimStart('#');
        if (id.Length == 0 || id.Length > 256)
        {
            return null;
        }

        var binary = root.Elements()
            .Where(element => element.Name.LocalName.Equals("binary", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(element => string.Equals(
                element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value,
                id,
                StringComparison.Ordinal));
        if (binary is null)
        {
            return null;
        }

        // Base64 uses four characters per three bytes; anything above this bound cannot be the cover anyway.
        var maxEncodedCharacters = (options.MaxCoverBytes * 4 / 3) + 8;
        if (binary.Value.Length > maxEncodedCharacters)
        {
            return null;
        }

        var encoded = new StringBuilder(binary.Value.Length);
        foreach (var character in binary.Value)
        {
            if (!char.IsWhiteSpace(character))
            {
                encoded.Append(character);
            }
        }

        byte[] cover;
        try
        {
            cover = Convert.FromBase64String(encoded.ToString());
        }
        catch (FormatException)
        {
            return null;
        }

        return EBookSignature.IsImage(cover) ? cover : null;
    }

    private static async Task<byte[]> ReadAllAsync(
        IMediaSource source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (source.Length <= 0)
        {
            throw new EBookDecodeException("the source is empty");
        }

        if (source.Length > maxBytes)
        {
            throw new EBookDecodeException("the FictionBook source exceeds the size bound");
        }

        using var output = new System.IO.MemoryStream((int)Math.Min(source.Length, 1024 * 1024));
        var offset = 0L;
        while (offset < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(ChunkBytes, source.Length - offset);
            var memory = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (memory.Length == 0)
            {
                throw new EBookDecodeException("the source ended before its declared length");
            }

            output.Write(memory.Span[..count]);
            offset += count;
        }

        return output.ToArray();
    }

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static string? Text(XElement? element) => element?.Value;
}
