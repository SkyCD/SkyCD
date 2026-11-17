using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using SkyCD.Plugin.DiskExplorer.Html.Exceptions;

namespace SkyCD.Plugin.DiskExplorer.Html;

/// <summary>
/// Writes Disk Explorer HTML report bundles: a root/index page plus a sibling
/// directory with one page per category/volume/folder, relative navigation links
/// and optional thumbnails. The target must be a file stream; the bundle is written
/// next to it. Output is deterministic in row encounter order.
/// </summary>
public static class DiskExplorerHtmlExporter
{
    private const string DefaultTitle = "Disk Explorer Report";

    public static async Task ExportAsync(
        Stream target,
        string? fileName,
        object? payload,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not FileStream fileTarget)
        {
            throw new DiskExplorerHtmlMalformedDocumentException(
                "HTML bundle export requires a file target so the sibling page directory can be written next to it.");
        }

        var request = NormalizeRequest(payload);
        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title.Trim()
            : !string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName))
                ? Path.GetFileNameWithoutExtension(fileName)
                : DefaultTitle;
        var encoding = ResolveEncoding(request.EncodingName);
        var rootPath = Path.GetFullPath(fileTarget.Name);
        var siblingDirectory = Path.Combine(
            Path.GetDirectoryName(rootPath) ?? Directory.GetCurrentDirectory(),
            Path.GetFileNameWithoutExtension(rootPath) ?? "report");
        Directory.CreateDirectory(siblingDirectory);
        var thumbsDirectory = Path.Combine(siblingDirectory, "thumbs");

        var rows = request.Rows.ToList();
        var tree = BuildTree(rows);
        var thumbCounter = 0;

        var siblingName = Path.GetFileName(siblingDirectory);
        using (var index = OpenTarget(target, title, title, encoding))
        {
            foreach (var root in tree.Roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rootPage = IsContainer(root) ? PageFileName(root, tree) : null;
                WriteItem(index.Writer, root, relativePage: rootPage, tree,
                    thumbsDirectory, ref thumbCounter, request, siblingName + "/", renderChildren: false, cancellationToken);
            }

            await index.Writer.WriteAsync("</div></body></html>");
            await index.Writer.FlushAsync(cancellationToken);
        }

        var queue = new Queue<ExportNode>(tree.Roots.Where(IsContainer));
        var total = Math.Max(1, CountContainers(tree));
        var done = 0;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = queue.Dequeue();
            var pageName = PageFileName(node, tree);
            var pagePath = Path.Combine(siblingDirectory, pageName);
            using (var page = OpenPage(pagePath, $"{title} - {node.Name}", title, encoding, cancellationToken))
            {
                WriteItem(page.Writer, node, relativePage: null, tree,
                    thumbsDirectory, ref thumbCounter, request, string.Empty, renderChildren: true, cancellationToken);
                if (tree.ChildrenByParent.TryGetValue(node.Id, out var direct))
                {
                    foreach (var child in direct)
                    {
                        if (IsContainer(child))
                        {
                            queue.Enqueue(child);
                        }
                    }
                }

                await page.Writer.WriteAsync("</div></body></html>");
                await page.Writer.FlushAsync(cancellationToken);
            }

            done++;
            progress?.Report(done * 100 / total);
        }

        progress?.Report(100);
    }

    private sealed record PageWriter(StreamWriter Writer) : IDisposable
    {
        public void Dispose() => Writer.Dispose();
    }

    private static PageWriter OpenTarget(Stream target, string pageTitle, string reportTitle, Encoding encoding)
    {
        var writer = new StreamWriter(target, encoding, leaveOpen: true) { NewLine = "\n" };
        WriteHead(writer, pageTitle, reportTitle, encoding);
        return new PageWriter(writer);
    }

    private static PageWriter OpenPage(
        string path,
        string pageTitle,
        string reportTitle,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = new StreamWriter(path, append: false, encoding) { NewLine = "\n" };
        WriteHead(writer, pageTitle, reportTitle, encoding);
        return new PageWriter(writer);
    }

    private static void WriteHead(TextWriter writer, string pageTitle, string reportTitle, Encoding encoding)
    {
        writer.Write("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\">\n");
        writer.Write("<html><head>\n");
        writer.Write($"<meta http-equiv=\"Content-Type\" content=\"text/html; charset={encoding.WebName}\">\n");
        writer.Write($"<title>{HttpUtility.HtmlEncode(pageTitle)}</title>\n");
        writer.Write("</head><body>\n");
        writer.Write($"<div class=\"dx-report\" data-title=\"{HttpUtility.HtmlAttributeEncode(reportTitle)}\">\n");
    }

    private static void WriteItem(
        TextWriter writer,
        ExportNode node,
        string? relativePage,
        ExportTree tree,
        string thumbsDirectory,
        ref int thumbCounter,
        DiskExplorerHtmlExportRequest request,
        string linkPrefix,
        bool renderChildren,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var itemType = ResolveItemType(node);
        writer.Write($"<div class=\"dx-item\" data-itemtype=\"{itemType}\"");
        writer.Write($" data-name=\"{HttpUtility.HtmlAttributeEncode(node.Name)}\"");
        writer.Write($" data-size=\"{node.Size.ToString(CultureInfo.InvariantCulture)}\"");

        var date = PropertyText(node, "Date");
        if (!string.IsNullOrWhiteSpace(date))
        {
            writer.Write($" data-date=\"{HttpUtility.HtmlAttributeEncode(date)}\"");
        }

        var time = PropertyText(node, "Time");
        if (!string.IsNullOrWhiteSpace(time))
        {
            writer.Write($" data-time=\"{HttpUtility.HtmlAttributeEncode(time)}\"");
        }

        var formatType = PropertyText(node, "FormatType");
        if (!string.IsNullOrWhiteSpace(formatType))
        {
            writer.Write($" data-type=\"{HttpUtility.HtmlAttributeEncode(formatType)}\"");
        }

        if (IsContainerKind(itemType))
        {
            var (files, folders) = CountChildren(tree, node);
            writer.Write($" data-files=\"{files}\" data-folders=\"{folders}\"");
        }

        var extraKeys = new List<string>();
        foreach (var pair in node.Properties)
        {
            if (pair.Key.StartsWith("DiskExplorerHtml.", StringComparison.Ordinal))
            {
                extraKeys.Add(pair.Key);
            }
        }

        extraKeys.Sort(StringComparer.Ordinal);
        foreach (var key in extraKeys)
        {
            var text = Convert.ToString(node.Properties[key], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                writer.Write($" data-{key["DiskExplorerHtml.".Length..].ToLowerInvariant()}=\"{HttpUtility.HtmlAttributeEncode(Truncate(text, 4096))}\"");
            }
        }

        writer.Write(">\n");
        if (relativePage is not null)
        {
            writer.Write($"<a class=\"dx-name\" href=\"{HttpUtility.HtmlAttributeEncode(linkPrefix + relativePage)}\">{HttpUtility.HtmlEncode(node.Name)}</a>\n");
        }
        else
        {
            writer.Write($"<span class=\"dx-name\">{HttpUtility.HtmlEncode(node.Name)}</span>\n");
        }

        var description = PropertyText(node, "Description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            writer.Write($"<div class=\"dx-desc\">{HttpUtility.HtmlEncode(description)}</div>\n");
        }

        if (request.IncludeThumbnails)
        {
            var thumb = CopyThumbnail(node, thumbsDirectory, ref thumbCounter);
            if (thumb is not null)
            {
                writer.Write($"<img class=\"dx-thumb\" src=\"{HttpUtility.HtmlAttributeEncode(thumb)}\" alt=\"\">\n");
            }
        }

        // Index entries are shells only: their children live on the linked subpage.
        // Subpage primaries inline files and link subfolders. Files always inline
        // their own children so no entry is lost.
        if (renderChildren && tree.ChildrenByParent.TryGetValue(node.Id, out var direct))
        {
            foreach (var child in direct.Where(child => !IsContainer(child)))
            {
                WriteInline(writer, child, tree, thumbsDirectory, ref thumbCounter, request, linkPrefix, cancellationToken);
            }

            if (relativePage is null)
            {
                foreach (var child in direct.Where(IsContainer))
                {
                    writer.Write($"<a class=\"dx-child\" href=\"{HttpUtility.HtmlAttributeEncode(linkPrefix + PageFileName(child, tree))}\">{HttpUtility.HtmlEncode(child.Name)}</a>\n");
                }
            }
        }

        writer.Write("</div>\n");
    }

    private static void WriteInline(
        TextWriter writer,
        ExportNode node,
        ExportTree tree,
        string thumbsDirectory,
        ref int thumbCounter,
        DiskExplorerHtmlExportRequest request,
        string linkPrefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var itemType = ResolveItemType(node);
        writer.Write($"<div class=\"dx-item\" data-itemtype=\"{itemType}\"");
        writer.Write($" data-name=\"{HttpUtility.HtmlAttributeEncode(node.Name)}\"");
        writer.Write($" data-size=\"{node.Size.ToString(CultureInfo.InvariantCulture)}\"");
        var formatType = PropertyText(node, "FormatType");
        if (!string.IsNullOrWhiteSpace(formatType))
        {
            writer.Write($" data-type=\"{HttpUtility.HtmlAttributeEncode(formatType)}\"");
        }

        writer.Write(">\n");
        writer.Write($"<span class=\"dx-name\">{HttpUtility.HtmlEncode(node.Name)}</span>\n");
        var description = PropertyText(node, "Description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            writer.Write($"<div class=\"dx-desc\">{HttpUtility.HtmlEncode(description)}</div>\n");
        }

        if (request.IncludeThumbnails)
        {
            var thumb = CopyThumbnail(node, thumbsDirectory, ref thumbCounter);
            if (thumb is not null)
            {
                writer.Write($"<img class=\"dx-thumb\" src=\"{HttpUtility.HtmlAttributeEncode(thumb)}\" alt=\"\">\n");
            }
        }

        if (tree.ChildrenByParent.TryGetValue(node.Id, out var direct))
        {
            foreach (var child in direct)
            {
                if (IsContainer(child))
                {
                    writer.Write($"<a class=\"dx-child\" href=\"{HttpUtility.HtmlAttributeEncode(linkPrefix + PageFileName(child, tree))}\">{HttpUtility.HtmlEncode(child.Name)}</a>\n");
                }
                else
                {
                    WriteInline(writer, child, tree, thumbsDirectory, ref thumbCounter, request, linkPrefix, cancellationToken);
                }
            }
        }

        writer.Write("</div>\n");
    }

    private static string? CopyThumbnail(ExportNode node, string thumbsDirectory, ref int thumbCounter)
    {
        var source = PropertyText(node, "ThumbnailFile");
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(thumbsDirectory);
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp"))
            {
                extension = ".bin";
            }

            var fileName = $"t{thumbCounter++:D4}{extension}";
            File.Copy(source, Path.Combine(thumbsDirectory, fileName), overwrite: true);
            return "thumbs/" + fileName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string PageFileName(ExportNode node, ExportTree tree)
    {
        var index = 0;
        var queue = new Queue<ExportNode>(tree.Roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (IsContainer(current))
            {
                if (ReferenceEquals(current, node))
                {
                    return $"p{index:D4}.html";
                }

                index++;
            }

            if (tree.ChildrenByParent.TryGetValue(current.Id, out var children))
            {
                foreach (var child in children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return "p0000.html";
    }

    private static int CountContainers(ExportTree tree)
    {
        var count = 0;
        var queue = new Queue<ExportNode>(tree.Roots);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (IsContainer(node))
            {
                count++;
            }

            if (tree.ChildrenByParent.TryGetValue(node.Id, out var children))
            {
                foreach (var child in children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return count;
    }

    private static (int Files, int Folders) CountChildren(ExportTree tree, ExportNode node)
    {
        if (!tree.ChildrenByParent.TryGetValue(node.Id, out var children))
        {
            return (0, 0);
        }

        var folders = children.Count(child => IsFolderKind(child.Type));
        return (children.Count - folders, folders);
    }

    private static bool IsFolderKind(string type) =>
        type is "folder" or "media" || string.Equals(type, "CategoryFolder", StringComparison.OrdinalIgnoreCase);

    private static bool IsContainerKind(string itemType) =>
        itemType is "Volume" or "CategoryFolder" or "Folder";

    private static bool IsContainer(ExportNode node) =>
        IsContainerKind(ResolveItemType(node));

    private static string ResolveItemType(ExportNode node)
    {
        foreach (var (name, value) in node.Properties)
        {
            if (name.Equals("ExportItemType", StringComparison.OrdinalIgnoreCase))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text) && DiskExplorerNames.TryParseItemType(text, out var parsed))
                {
                    return DiskExplorerNames.ItemTypeToString(parsed);
                }
            }
        }

        return node.Type switch
        {
            "media" => "Volume",
            "folder" => IsCategory(node) ? "CategoryFolder" : "Folder",
            _ => "File"
        };
    }

    private static bool IsCategory(ExportNode node) =>
        node.Properties.Keys.Any(key => key.Equals("Category", StringComparison.OrdinalIgnoreCase));

    private static string? PropertyText(ExportNode node, string key)
    {
        foreach (var (name, value) in node.Properties)
        {
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static DiskExplorerHtmlExportRequest NormalizeRequest(object? payload)
    {
        if (payload is DiskExplorerHtmlExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Rows);
            return request;
        }

        return new DiskExplorerHtmlExportRequest { Rows = CoerceRows(payload) };
    }

    private static IEnumerable<Dictionary<string, object?>> CoerceRows(object? payload)
    {
        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            return rows;
        }

        if (payload is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } array)
        {
            return array.EnumerateArray().Select(element =>
                element.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? element.EnumerateObject().ToDictionary(
                        property => property.Name,
                        property => ReadJsonValue(property.Value),
                        StringComparer.OrdinalIgnoreCase)
                    : throw new DiskExplorerHtmlMalformedDocumentException("JSON array items must be objects."));
        }

        throw new DiskExplorerHtmlMalformedDocumentException("Export payload must be catalog rows or a JSON array of objects.");
    }

    private static object? ReadJsonValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                return null;
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.Number:
                return element.GetRawText();
            case System.Text.Json.JsonValueKind.True:
                return bool.TrueString;
            case System.Text.Json.JsonValueKind.False:
                return bool.FalseString;
            default:
                return element.GetRawText();
        }
    }

    private sealed record ExportNode(
        string Id,
        string? ParentId,
        string Name,
        string Type,
        long Size,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed record ExportTree(IReadOnlyList<ExportNode> Roots, IReadOnlyDictionary<string, List<ExportNode>> ChildrenByParent);

    private static ExportTree BuildTree(List<Dictionary<string, object?>> rows)
    {
        var nodes = rows.Select(ReadNode).ToList();
        var byId = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var childrenByParent = new Dictionary<string, List<ExportNode>>(StringComparer.Ordinal);
        var roots = new List<ExportNode>();
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.ParentId) && byId.ContainsKey(node.ParentId))
            {
                if (!childrenByParent.TryGetValue(node.ParentId, out var siblings))
                {
                    siblings = [];
                    childrenByParent[node.ParentId] = siblings;
                }

                siblings.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return new ExportTree(roots, childrenByParent);
    }

    private static ExportNode ReadNode(Dictionary<string, object?> row)
    {
        return new ExportNode(
            Id: ReadText(row, "id", "NodeId") ?? string.Empty,
            ParentId: ReadText(row, "parentId", "ParentId"),
            Name: ReadText(row, "name", "Name") ?? "Unnamed",
            Type: (ReadText(row, "type", "Kind") ?? "file").Trim().ToLowerInvariant(),
            Size: ReadSize(row),
            Properties: ReadProperties(row));
    }

    private static string? ReadText(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value))
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static long ReadSize(Dictionary<string, object?> row)
    {
        var text = ReadText(row, "size", "SizeBytes");
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return Math.Max(0L, integer);
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            return Math.Max(0L, (long)number);
        }

        return 0L;
    }

    private static IReadOnlyDictionary<string, object?> ReadProperties(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "properties", "Properties" })
        {
            if (row.TryGetValue(key, out var raw) && raw is IDictionary<string, object?> dict)
            {
                return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static Encoding ResolveEncoding(string name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "windows-1252" or "cp1252" or "1252" => ResolveCodePage(1252),
            _ => throw new DiskExplorerHtmlMalformedDocumentException($"Unsupported HTML encoding '{name}'.")
        };
    }

    private static Encoding ResolveCodePage(int codePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new DiskExplorerHtmlMalformedDocumentException($"Code page {codePage} is not available on this platform.");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…[truncated]";
}

