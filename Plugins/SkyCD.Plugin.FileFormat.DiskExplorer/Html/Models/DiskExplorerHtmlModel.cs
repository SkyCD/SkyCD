using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using AngleSharp.Dom;
using SkyCD.Plugin.FileFormat.DiskExplorer.Html.Parsing;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html.Models;

/// <summary>
/// Extracted page model: top-level items plus legacy fallbacks. Handles the canonical
/// <c>dx-*</c> markup and a legacy fallback (headings, definition lists, labeled
/// tables, plain links) so historical exports remain readable.
/// </summary>
internal sealed record ItemModel
{
    public string? Name { get; init; }
    public DiskExplorerElementKind? Kind { get; init; }
    public string? RawItemType { get; init; }
    public string? PageHref { get; init; }
    public string? ThumbSrc { get; init; }
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Extra { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ItemModel> NestedItems { get; init; } = [];
    public List<(string Href, string Name)> ChildLinks { get; init; } = [];
    public bool HasChildContent => NestedItems.Count > 0 || ChildLinks.Count > 0;
}

internal sealed record PageModel(string? Title, List<ItemModel> TopItems)
{
    public static PageModel Read(IDocument document, string pageName)
    {
        var title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title.Trim();
        var dxItems = document.QuerySelectorAll("div.dx-item").OfType<IElement>().ToList();
        if (dxItems.Count > 0)
        {
            return new PageModel(title, ReadCanonicalItems(dxItems));
        }

        var legacy = ReadLegacyItem(document, pageName);
        return new PageModel(title, legacy is null ? [] : [legacy]);
    }

    private static List<ItemModel> ReadCanonicalItems(List<IElement> elements)
    {
        var models = new Dictionary<IElement, ItemModel>();
        foreach (var element in elements)
        {
            if (ReadCanonicalItem(element) is { } model)
            {
                models[element] = model;
            }
        }

        var tops = new List<ItemModel>();
        foreach (var (element, model) in models)
        {
            var ancestor = element.ParentElement;
            ItemModel? owner = null;
            while (ancestor is not null)
            {
                if (ancestor.ClassList?.Contains("dx-item") == true && models.TryGetValue(ancestor, out var candidate))
                {
                    owner = candidate;
                    break;
                }

                ancestor = ancestor.ParentElement;
            }

            if (owner is null)
            {
                tops.Add(model);
            }
            else
            {
                owner.NestedItems.Add(model);
            }
        }

        return tops;
    }

    private static ItemModel? ReadCanonicalItem(IElement element)
    {
        var rawItemType = element.GetAttribute("data-itemtype");
        DiskExplorerElementKind? kind = DiskExplorerNames.TryParseItemType(rawItemType, out var parsed)
            ? parsed
            : null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes)
        {
            if (attribute.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
            {
                var key = attribute.Name["data-".Length..];
                if (!key.Equals("itemtype", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    fields[key] = attribute.Value ?? string.Empty;
                }
            }
        }

        var nameLink = element.QuerySelector("a.dx-name");
        var name = element.GetAttribute("data-name") ?? nameLink?.TextContent ?? string.Empty;
        var description = element.QuerySelector("div.dx-desc")?.TextContent;
        var thumb = element.QuerySelector("img.dx-thumb")?.GetAttribute("src");
        var childLinks = element.QuerySelectorAll("a.dx-child")
            .OfType<IElement>()
            .Select(link => (Href: link.GetAttribute("href") ?? string.Empty, Name: (link.TextContent ?? string.Empty).Trim()))
            .Where(link => !string.IsNullOrWhiteSpace(link.Href))
            .ToList();

        if (!string.IsNullOrWhiteSpace(description))
        {
            fields["description"] = description.Trim();
        }

        var trimmedName = name.Trim();
        var pageHref = nameLink?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(trimmedName)
            && fields.Count == 0
            && string.IsNullOrWhiteSpace(pageHref)
            && childLinks.Count == 0
            && string.IsNullOrWhiteSpace(thumb))
        {
            return null;
        }

        return new ItemModel
        {
            Name = trimmedName,
            Kind = kind,
            RawItemType = rawItemType,
            PageHref = pageHref,
            ThumbSrc = string.IsNullOrWhiteSpace(thumb) ? null : thumb,
            Fields = fields,
            ChildLinks = childLinks
        };
    }

    private static ItemModel? ReadLegacyItem(IDocument document, string pageName)
    {
        var heading = document.QuerySelector("h1")?.TextContent?.Trim();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in document.QuerySelectorAll("dl"))
        {
            var terms = list.QuerySelectorAll("dt");
            var definitions = list.QuerySelectorAll("dd");
            for (var i = 0; i < Math.Min(terms.Length, definitions.Length); i++)
            {
                var label = NormalizeLabel(terms[i].TextContent);
                if (MapLegacyField(label) is string mapped && !fields.ContainsKey(mapped))
                {
                    fields[mapped] = (definitions[i].TextContent ?? string.Empty).Trim();
                }
            }
        }

        foreach (var table in document.QuerySelectorAll("table"))
        {
            foreach (var row in table.QuerySelectorAll("tr"))
            {
                var cells = row.QuerySelectorAll("th, td");
                if (cells.Length == 2)
                {
                    var label = NormalizeLabel(cells[0].TextContent);
                    if (MapLegacyField(label) is string mapped && !fields.ContainsKey(mapped))
                    {
                        fields[mapped] = (cells[1].TextContent ?? string.Empty).Trim();
                    }
                }
            }
        }

        var links = document.QuerySelectorAll("a[href]")
            .OfType<IElement>()
            .Select(link => (Href: link.GetAttribute("href") ?? string.Empty, Name: (link.TextContent ?? string.Empty).Trim()))
            .Where(link => !string.IsNullOrWhiteSpace(link.Href))
            .ToList();
        var thumb = document.QuerySelectorAll("img[src]")
            .OfType<IElement>()
            .Select(img => img.GetAttribute("src"))
            .FirstOrDefault(src => !string.IsNullOrWhiteSpace(src));

        if (string.IsNullOrWhiteSpace(heading)
            && fields.Count == 0
            && links.Count == 0
            && string.IsNullOrWhiteSpace(thumb))
        {
            return null;
        }

        return new ItemModel
        {
            Name = !string.IsNullOrWhiteSpace(heading) ? heading : Path.GetFileNameWithoutExtension(pageName),
            Fields = fields,
            ThumbSrc = thumb,
            ChildLinks = links
        };
    }

    private static string NormalizeLabel(string? label) =>
        (label ?? string.Empty).Trim().TrimEnd(':', ' ').ToLowerInvariant();

    private static string? MapLegacyField(string label) => label switch
    {
        "name" or "title" or "filename" or "file name" => "name",
        "size" or "sizebytes" or "file size" => "size",
        "files" or "file count" => "files",
        "folders" or "folder count" or "directories" => "folders",
        "date" or "modified" or "last modified" or "creation date" or "created" => "date",
        "time" or "last modified time" => "time",
        "type" or "kind" or "file type" => "type",
        "description" or "comment" or "notes" or "desc" => "description",
        _ => null
    };
}

internal sealed record NodeRef(string Id, string VolumeKey, string FolderPath, string PageRef);

internal sealed class NodeBuilder(
    List<Dictionary<string, object?>> nodes,
    HashSet<string> usedIds,
    DiskExplorerHtmlImportSummary summary,
    string? reportRoot)
{
    private int assetCount;

    public int NodeCount => nodes.Count;

    public int AssetCount
    {
        get => assetCount;
        set => assetCount = value;
    }

    public NodeRef AddRoot(
        ItemModel item,
        DiskExplorerElementKind defaultKind,
        string pageRef,
        string pageDirectory,
        CancellationToken cancellationToken)
    {
        return AddNode(item, parent: null, defaultKind, pageRef, pageDirectory, cancellationToken);
    }

    public NodeRef AddChild(
        ItemModel item,
        NodeRef parent,
        DiskExplorerElementKind defaultKind,
        string pageRef,
        string pageDirectory,
        CancellationToken cancellationToken)
    {
        return AddNode(item, parent, defaultKind, pageRef, pageDirectory, cancellationToken);
    }

    public void MergePrimary(NodeRef node, ItemModel primary, string pagePath, CancellationToken cancellationToken)
    {
        var row = nodes.FirstOrDefault(candidate =>
            string.Equals(Convert.ToString(candidate["id"]), node.Id, StringComparison.Ordinal));
        if (row is null)
        {
            return;
        }

        var properties = (Dictionary<string, object?>)row["properties"]!;
        var pageDirectory = Path.GetDirectoryName(pagePath);
        foreach (var (key, value) in ExtractProperties(primary,
                     pageDirectory ?? string.Empty,
                     checkAssets: false, cancellationToken))
        {
            if (!properties.ContainsKey(key))
            {
                properties[key] = value;
            }
        }
    }

    public void AddFileRef(
        string linkName,
        string href,
        NodeRef parent,
        string targetFullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = !string.IsNullOrWhiteSpace(linkName) ? linkName.Trim() : Path.GetFileName(targetFullPath);
        var relative = string.IsNullOrEmpty(parent.FolderPath) ? name : $"{parent.FolderPath}/{name}";
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DiskExplorerHtmlSource"] = "DiskExplorerHtml",
            ["HtmlPage"] = parent.PageRef,
            ["LinkedPath"] = reportRoot is null
                ? href
                : DiskExplorerHtmlPath.RelativeToRoot(reportRoot, targetFullPath)
        };
        if (!File.Exists(targetFullPath))
        {
            summary.MissingAssets++;
            summary.Partial = true;
            summary.Notes.Add($"Missing linked file '{href}'.");
        }

        nodes.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = UniqueId($"file:{parent.VolumeKey}/{relative}"),
            ["name"] = name,
            ["parentId"] = parent.Id,
            ["type"] = "file",
            ["size"] = 0L,
            ["childrenCount"] = 0L,
            ["properties"] = properties
        });
        summary.Files++;
    }

    public void ResolveThumbnail(
        Dictionary<string, object?> properties,
        string? thumbSrc,
        string pageDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(thumbSrc))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (reportRoot is null)
        {
            summary.Partial = true;
            summary.Notes.Add($"Thumbnail '{thumbSrc}' left unresolved without a bundle root.");
            properties["ThumbnailReference"] = thumbSrc.Trim();
            return;
        }

        if (assetCount >= DiskExplorerHtmlReader.MaxAssets)
        {
            summary.Partial = true;
            summary.Notes.Add("Asset limit reached; remaining thumbnails were skipped.");
            return;
        }

        assetCount++;
        if (DiskExplorerHtmlPath.IsRemoteOrSpecialScheme(thumbSrc))
        {
            summary.SkippedExternalLinks++;
            summary.Partial = true;
            summary.Notes.Add($"Skipped remote thumbnail '{thumbSrc}'.");
            return;
        }

        if (!DiskExplorerHtmlPath.TryResolveWithinRoot(reportRoot, pageDirectory, thumbSrc,
                out var fullPath, out _))
        {
            summary.MissingAssets++;
            summary.Partial = true;
            return;
        }

        if (DiskExplorerHtmlPath.IsReparsePoint(fullPath) || !File.Exists(fullPath))
        {
            summary.MissingAssets++;
            summary.Partial = true;
            summary.Notes.Add($"Missing thumbnail '{thumbSrc}'.");
            return;
        }

        try
        {
            if (new FileInfo(fullPath).Length > DiskExplorerHtmlReader.MaxThumbnailBytes)
            {
                summary.Notes.Add($"Thumbnail '{thumbSrc}' exceeds the size limit.");
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            summary.MissingAssets++;
            summary.Partial = true;
            return;
        }

        properties["HasThumbnail"] = "true";
        properties["ThumbnailFile"] = DiskExplorerHtmlPath.RelativeToRoot(reportRoot, fullPath);
    }

    private NodeRef AddNode(
        ItemModel item,
        NodeRef? parent,
        DiskExplorerElementKind defaultKind,
        string pageRef,
        string pageDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kind = item.Kind ?? defaultKind;
        var name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name.Trim() : "Unnamed";
        var volumeKey = parent?.VolumeKey ?? name;
        string nodeKind;
        string id;
        string folderPath;
        switch (kind)
        {
            case DiskExplorerElementKind.Volume:
                nodeKind = "media";
                id = UniqueId($"volume:{name}");
                folderPath = string.Empty;
                volumeKey = name;
                summary.Volumes++;
                break;
            case DiskExplorerElementKind.CategoryFolder:
            case DiskExplorerElementKind.Folder:
                nodeKind = "folder";
                folderPath = parent is null || string.IsNullOrEmpty(parent.FolderPath) ? name : $"{parent.FolderPath}/{name}";
                id = UniqueId($"folder:{volumeKey}/{folderPath}");
                summary.Folders++;
                break;
            default:
                nodeKind = "file";
                folderPath = parent?.FolderPath ?? string.Empty;
                var relative = parent is null || string.IsNullOrEmpty(parent.FolderPath) ? name : $"{parent.FolderPath}/{name}";
                id = UniqueId($"file:{volumeKey}/{relative}");
                summary.Files++;
                break;
        }

        var properties = ExtractProperties(item, pageDirectory, checkAssets: true, cancellationToken: cancellationToken);
        properties["DiskExplorerHtmlSource"] = "DiskExplorerHtml";
        properties["HtmlPage"] = pageRef;
        if (!string.IsNullOrWhiteSpace(item.RawItemType))
        {
            properties["ExportItemType"] = item.RawItemType;
        }

        nodes.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["name"] = name,
            ["parentId"] = parent?.Id,
            ["type"] = nodeKind,
            ["size"] = ReadSize(item),
            ["childrenCount"] = 0L,
            ["properties"] = properties
        });
        return new NodeRef(id, volumeKey, folderPath, pageRef);
    }

    private Dictionary<string, object?> ExtractProperties(
        ItemModel item,
        string pageDirectory,
        bool checkAssets,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in item.Fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "name":
                    break;
                case "size":
                    break;
                case "files":
                    properties["FilesCount"] = value.Trim();
                    break;
                case "folders":
                    properties["FoldersCount"] = value.Trim();
                    break;
                case "date":
                    properties["Date"] = value.Trim();
                    if (NormalizeDate(value) is string normalizedDate)
                    {
                        properties["DateNormalized"] = normalizedDate;
                    }

                    break;
                case "time":
                    properties["Time"] = value.Trim();
                    if (NormalizeTime(value) is string normalizedTime)
                    {
                        properties["TimeNormalized"] = normalizedTime;
                    }

                    break;
                case "type":
                    properties["FormatType"] = value.Trim();
                    break;
                case "description":
                    properties["Description"] = Truncate(value.Trim(), DiskExplorerHtmlReader.MaxDescriptionChars);
                    break;
                default:
                    properties[$"DiskExplorerHtml.{key}"] = Truncate(value.Trim(), DiskExplorerHtmlReader.MaxTextFieldChars);
                    break;
            }
        }

        foreach (var (key, value) in item.Extra)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[key] = Truncate(value.Trim(), DiskExplorerHtmlReader.MaxTextFieldChars);
            }
        }

        if (checkAssets)
        {
            ResolveThumbnail(properties, item.ThumbSrc, pageDirectory, cancellationToken);
        }

        return properties;
    }

    private string UniqueId(string candidate)
    {
        if (usedIds.Add(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (!usedIds.Add($"{candidate} ~{suffix}"))
        {
            suffix++;
        }

        summary.Notes.Add($"Duplicate entry id '{candidate}' disambiguated.");
        return $"{candidate} ~{suffix}";
    }

    private static long ReadSize(ItemModel item)
    {
        if (!item.Fields.TryGetValue("size", out var text) || string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        var cleaned = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return ulong.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? (long)Math.Min(value, (ulong)long.MaxValue)
            : 0L;
    }

    private static string? NormalizeDate(string value)
    {
        var trimmed = value.Trim();
        string[] formats =
        [
            "o", "yyyy-MM-dd", "MM/dd/yyyy", "dd.MM.yyyy", "dd/MM/yyyy", "yyyyMMdd",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
        ];
        return DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeTime(string value)
    {
        var trimmed = value.Trim();
        string[] formats = ["o", "HH:mm:ss", "HH:mm", "hh:mm:ss tt", "hh:mm tt"];
        return DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : null;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "…[truncated]";
    }
}
