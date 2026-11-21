using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SkyCD.Plugin.EBook.Parsing;

namespace SkyCD.Plugin.EBook.Models;

/// <summary>
/// The parts of an EPUB package document the plugin uses: Dublin Core metadata, the manifest, the EPUB 2
/// cover meta element and the guide. Elements are matched by local name, because the namespace prefix is
/// author-chosen and a package that renames it must still be read.
/// </summary>
internal sealed class EpubPackage
{
    private const string ImageCoverProperty = "cover-image";

    private EpubPackage()
    {
    }

    public string? Title { get; private init; }

    public IReadOnlyList<string> Authors { get; private init; } = [];

    public string? Publisher { get; private init; }

    public string? Language { get; private init; }

    public string? Subject { get; private init; }

    public string? Description { get; private init; }

    public string? Identifier { get; private init; }

    public DateTimeOffset? PublishedAt { get; private init; }

    private IReadOnlyList<ManifestItem> Manifest { get; init; } = [];

    private string? CoverId { get; init; }

    private string? GuideCoverHref { get; init; }

    public static EpubPackage? Parse(XDocument document, int maxFieldCharacters)
    {
        var package = document.Root;
        if (package is null || !string.Equals(package.Name.LocalName, "package", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metadata = Child(package, "metadata");
        var values = metadata is null ? [] : Values(metadata, maxFieldCharacters);
        var manifest = Child(package, "manifest");
        var items = manifest is null
            ? []
            : manifest.Descendants()
                .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                .Select(element => new ManifestItem(
                    Attribute(element, "id") ?? string.Empty,
                    Attribute(element, "href") ?? string.Empty,
                    Attribute(element, "media-type"),
                    Attribute(element, "properties")))
                .Where(item => item.Href.Length > 0)
                .ToList();

        var coverId = metadata?.Descendants()
            .Where(element => element.Name.LocalName.Equals("meta", StringComparison.OrdinalIgnoreCase))
            .Where(element => string.Equals(Attribute(element, "name"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(element => Attribute(element, "content"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var guideCoverHref = Child(package, "guide")?.Descendants()
            .Where(element => element.Name.LocalName.Equals("reference", StringComparison.OrdinalIgnoreCase))
            .Where(element => string.Equals(Attribute(element, "type"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(element => Attribute(element, "href"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new EpubPackage
        {
            Title = values.GetValueOrDefault("title")?.FirstOrDefault(),
            Authors = values.GetValueOrDefault("creator") ?? [],
            Publisher = values.GetValueOrDefault("publisher")?.FirstOrDefault(),
            Language = values.GetValueOrDefault("language")?.FirstOrDefault(),
            Subject = values.GetValueOrDefault("subject")?.FirstOrDefault(),
            Description = values.GetValueOrDefault("description")?.FirstOrDefault(),
            Identifier = values.GetValueOrDefault("identifier")?.FirstOrDefault(),
            PublishedAt = EBookText.ParseTimestamp(values.GetValueOrDefault("date")?.FirstOrDefault()),
            Manifest = items,
            CoverId = coverId,
            GuideCoverHref = guideCoverHref
        };
    }

    /// <summary>
    /// Finds the manifest href of the cover image: EPUB 3's <c>cover-image</c> property first, then the EPUB 2
    /// <c>meta name="cover"</c> id, then a guide reference that points at an image. A guide reference to a
    /// wrapper page is not a cover image and is skipped rather than guessed at.
    /// </summary>
    public string? FindCoverHref()
    {
        var propertyCover = Manifest.FirstOrDefault(item =>
            item.Properties is not null &&
            item.Properties.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(ImageCoverProperty, StringComparer.OrdinalIgnoreCase));
        if (propertyCover is not null)
        {
            return propertyCover.Href;
        }

        var idCover = CoverId is null
            ? null
            : Manifest.FirstOrDefault(item => string.Equals(item.Id, CoverId, StringComparison.Ordinal))
                ?? Manifest.FirstOrDefault(item => string.Equals(item.Id, CoverId, StringComparison.OrdinalIgnoreCase));
        if (idCover is not null)
        {
            return idCover.Href;
        }

        if (GuideCoverHref is null)
        {
            return null;
        }

        var referenced = Manifest.FirstOrDefault(item => item.Href == GuideCoverHref);
        if (referenced is not null)
        {
            return IsImageType(referenced) ? referenced.Href : null;
        }

        return HasImageExtension(GuideCoverHref) ? GuideCoverHref : null;
    }

    private static bool IsImageType(ManifestItem item) =>
        item.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasImageExtension(string href)
    {
        var extension = System.IO.Path.GetExtension(href);
        return extension.Length > 0 && new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Collects Dublin Core values by local name, keeping document order and dropping empty ones.</summary>
    private static Dictionary<string, List<string>> Values(XElement metadata, int maxFieldCharacters)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in metadata.Descendants())
        {
            var text = EBookText.Normalize(element.Value, maxFieldCharacters);
            if (text is null)
            {
                continue;
            }

            var key = element.Name.LocalName.ToLowerInvariant();
            if (!values.TryGetValue(key, out var list))
            {
                list = [];
                values[key] = list;
            }

            list.Add(text);
        }

        return values;
    }

    private static XElement? Child(XElement element, string localName) =>
        element.Descendants().FirstOrDefault(candidate =>
            candidate.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    internal sealed record ManifestItem(string Id, string Href, string? MediaType, string? Properties);
}
