using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AngleSharp.Html.Parser;
using SkyCD.Plugin.DiskExplorer.Html.Exceptions;

namespace SkyCD.Plugin.DiskExplorer.Html;

/// <summary>
/// Imports Disk Explorer HTML report bundles: a root/index page plus a sibling
/// directory of per-category/volume/folder pages. Follows only relative links
/// contained within the report root, deduplicates canonical pages, detects cycles
/// and never fetches remote URLs or executes page content (AngleSharp parses only).
/// Without a source file path only the single supplied page is imported as partial.
/// </summary>
public static class DiskExplorerHtmlReader
{
    public const int MaxPages = 10000;
    public const int MaxDepth = 64;
    public const long MaxPageBytes = 8 * 1024 * 1024;
    public const int MaxAssets = 50000;
    public const long MaxThumbnailBytes = 8 * 1024 * 1024;
    public const int MaxDescriptionChars = 32768;
    public const int MaxTextFieldChars = 65536;

    public static DiskExplorerHtmlReportDocument Parse(
        Stream source,
        string? sourcePath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var rootBytes = ReadBounded(source, MaxPageBytes, "report root page");
        var reportRoot = !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath)
            ? Path.GetDirectoryName(Path.GetFullPath(sourcePath))!
            : null;
        var rootName = !string.IsNullOrWhiteSpace(sourcePath)
            ? Path.GetFileName(sourcePath)
            : "report.html";
        var summary = new DiskExplorerHtmlImportSummary();
        var nodes = new List<Dictionary<string, object?>>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var parser = new HtmlParser();

        var rootEncoding = DetectEncoding(rootBytes);
        summary.EncodingName = rootEncoding.WebName;
        var rootPage = PageModel.Read(parser.ParseDocument(rootEncoding.GetString(rootBytes)), rootName);
        summary.Title = rootPage.Title;
        var builder = new NodeBuilder(nodes, usedIds, summary, reportRoot);

        if (reportRoot is null)
        {
            summary.Partial = true;
            summary.Notes.Add("No source file path: imported the single supplied page without following bundle links.");
            foreach (var item in rootPage.TopItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = builder.AddRoot(item, DiskExplorerElementKind.Volume, rootName, string.Empty, cancellationToken);
                AttachInline(item, node, string.Empty, builder, cancellationToken);
            }
        }
        else
        {
            progress?.Report(0);
            var traverser = new Traverser(reportRoot, summary, builder, parser, progress);
            traverser.RunRoot(rootPage, rootName, sourcePath!, cancellationToken);
        }

        if (nodes.Count == 0)
        {
            throw new DiskExplorerHtmlEmptyReportException();
        }

        ApplyChildrenCounts(nodes);
        progress?.Report(100);
        return new DiskExplorerHtmlReportDocument(nodes, summary);
    }

    private static void ApplyChildrenCounts(List<Dictionary<string, object?>> nodes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var parentId = Convert.ToString(node["parentId"], System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                counts[parentId] = counts.TryGetValue(parentId, out var current) ? current + 1 : 1;
            }
        }

        foreach (var node in nodes)
        {
            var id = Convert.ToString(node["id"], System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(id) && counts.TryGetValue(id, out var count))
            {
                node["childrenCount"] = (long)count;
            }
        }
    }

    private static void AttachInline(
        ItemModel item,
        NodeRef node,
        string pageDirectory,
        NodeBuilder builder,
        CancellationToken cancellationToken)
    {
        foreach (var nested in item.NestedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = builder.AddChild(nested, node,
                nested.HasChildContent ? DiskExplorerElementKind.Folder : DiskExplorerElementKind.File,
                node.PageRef, pageDirectory, cancellationToken);
            AttachInline(nested, child, pageDirectory, builder, cancellationToken);
        }
    }

    private sealed class Traverser
    {
        private readonly string reportRoot;
        private readonly DiskExplorerHtmlImportSummary summary;
        private readonly NodeBuilder builder;
        private readonly HtmlParser parser;
        private readonly IProgress<int>? progress;
        private readonly HashSet<string> visitedPages = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        public Traverser(
            string reportRoot,
            DiskExplorerHtmlImportSummary summary,
            NodeBuilder builder,
            HtmlParser parser,
            IProgress<int>? progress)
        {
            this.reportRoot = reportRoot;
            this.summary = summary;
            this.builder = builder;
            this.parser = parser;
            this.progress = progress;
        }

        public void RunRoot(PageModel rootPage, string rootName, string sourcePath, CancellationToken cancellationToken)
        {
            var rootFullPath = Path.GetFullPath(sourcePath);
            visitedPages.Add(rootFullPath);
            summary.PagesVisited++;
            var rootDirectory = Path.GetDirectoryName(rootFullPath) ?? reportRoot;

            foreach (var item in rootPage.TopItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = builder.AddRoot(item, DiskExplorerElementKind.Volume, rootName, rootDirectory, cancellationToken);
                VisitOwnPage(item, node, rootDirectory, rootFullPath, depth: 1, cancellationToken);
                VisitChildLinks(item, node, rootDirectory, rootFullPath, depth: 1, cancellationToken);
                VisitNested(item, node, rootDirectory, cancellationToken);
            }

            if (builder.NodeCount == 0)
            {
                summary.Partial = true;
                summary.Notes.Add("Index page contains no recognizable items; imported as an empty partial report.");
            }
        }

        private void VisitOwnPage(
            ItemModel item,
            NodeRef node,
            string pageDirectory,
            string? currentPage,
            int depth,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(item.PageHref))
            {
                return;
            }

            VisitPageTarget(item.PageHref, item.Name ?? string.Empty, node,
                pageDirectory, currentPage, depth, cancellationToken);
        }

        private void VisitChildLinks(
            ItemModel item,
            NodeRef node,
            string pageDirectory,
            string? currentPage,
            int depth,
            CancellationToken cancellationToken)
        {
            foreach (var (href, name) in item.ChildLinks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldCreateNode(href, pageDirectory))
                {
                    continue;
                }

                var child = builder.AddChild(new ItemModel { Name = name }, node,
                    DiskExplorerElementKind.Folder, node.PageRef, pageDirectory, cancellationToken);
                VisitPageTarget(href, name, node: child,
                    pageDirectory, currentPage, depth, cancellationToken);
            }
        }

        private bool ShouldCreateNode(string href, string pageDirectory)
        {
            if (!DiskExplorerHtmlPath.TryResolveWithinRoot(reportRoot, pageDirectory, href,
                    out var target, out var failure))
            {
                if (DiskExplorerHtmlPath.IsRemoteOrSpecialScheme(href))
                {
                    summary.SkippedExternalLinks++;
                }
                else
                {
                    summary.SkippedUnsafeLinks++;
                }

                summary.Partial = true;
                summary.Notes.Add($"Skipped unsafe link '{href}': {failure}");
                return false;
            }

            if (DiskExplorerHtmlPath.IsReparsePoint(target))
            {
                summary.SkippedUnsafeLinks++;
                summary.Partial = true;
                summary.Notes.Add($"Skipped reparse-point target '{href}'.");
                return false;
            }

            var extension = Path.GetExtension(target);
            var isHtml = extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
            if (isHtml && visiting.Contains(target))
            {
                summary.CyclesDetected++;
                summary.Notes.Add($"Cycle detected at '{DiskExplorerHtmlPath.RelativeToRoot(reportRoot, target)}'.");
                return false;
            }

            if (isHtml && visitedPages.Contains(target))
            {
                return false;
            }

            return true;
        }

        private void VisitNested(
            ItemModel item,
            NodeRef node,
            string pageDirectory,
            CancellationToken cancellationToken)
        {
            foreach (var nested in item.NestedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(nested.PageHref))
                {
                    var child = builder.AddChild(nested, node,
                        nested.HasChildContent ? DiskExplorerElementKind.Folder : DiskExplorerElementKind.File,
                        node.PageRef, pageDirectory, cancellationToken);
                    VisitPageTarget(nested.PageHref, nested.Name ?? "", node: child,
                        pageDirectory, currentPage: null, depth: 2, cancellationToken);
                    VisitChildLinks(nested, child, pageDirectory, currentPage: null, depth: 2, cancellationToken);
                    VisitNested(nested, child, pageDirectory, cancellationToken);
                    continue;
                }

                var inline = builder.AddChild(nested, node,
                    nested.HasChildContent ? DiskExplorerElementKind.Folder : DiskExplorerElementKind.File,
                    node.PageRef, pageDirectory, cancellationToken);
                VisitChildLinks(nested, inline, pageDirectory, currentPage: null, depth: 2, cancellationToken);
                VisitNested(nested, inline, pageDirectory, cancellationToken);
            }
        }

        private void VisitPageTarget(
            string href,
            string fileRefName,
            NodeRef node,
            string pageDirectory,
            string? currentPage,
            int depth,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DiskExplorerHtmlPath.TryResolveWithinRoot(reportRoot, pageDirectory, href,
                    out var target, out var failure))
            {
                if (DiskExplorerHtmlPath.IsRemoteOrSpecialScheme(href))
                {
                    summary.SkippedExternalLinks++;
                }
                else
                {
                    summary.SkippedUnsafeLinks++;
                }

                summary.Partial = true;
                summary.Notes.Add($"Skipped unsafe link '{href}': {failure}");
                return;
            }

            if (DiskExplorerHtmlPath.IsReparsePoint(target))
            {
                summary.SkippedUnsafeLinks++;
                summary.Partial = true;
                summary.Notes.Add($"Skipped reparse-point target '{href}'.");
                return;
            }

            if (!IsHtmlPath(target))
            {
                builder.AddFileRef(fileRefName, href, node, target, cancellationToken);
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentPage)
                && string.Equals(target, currentPage, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (visiting.Contains(target))
            {
                summary.CyclesDetected++;
                summary.Notes.Add($"Cycle detected at '{DiskExplorerHtmlPath.RelativeToRoot(reportRoot, target)}'.");
                return;
            }

            if (visitedPages.Contains(target))
            {
                return;
            }

            if (!File.Exists(target))
            {
                summary.MissingPages++;
                summary.Partial = true;
                summary.Notes.Add($"Missing page '{href}'.");
                return;
            }

            if (depth >= MaxDepth)
            {
                summary.Partial = true;
                summary.Notes.Add($"Maximum traversal depth reached at '{href}'.");
                return;
            }

            if (visitedPages.Count >= MaxPages)
            {
                throw new DiskExplorerHtmlMalformedDocumentException(
                    $"Report exceeds the maximum supported page count of {MaxPages}.");
            }

            visiting.Add(target);
            try
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(target);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    summary.MissingPages++;
                    summary.Partial = true;
                    summary.Notes.Add($"Unreadable page '{href}'.");
                    return;
                }

                if (bytes.Length > MaxPageBytes)
                {
                    summary.Partial = true;
                    summary.Notes.Add($"Page '{href}' exceeds the size limit and was skipped.");
                    return;
                }

                visitedPages.Add(target);
                summary.PagesVisited++;
                progress?.Report(Math.Min(99, summary.PagesVisited * 100 / Math.Max(1, visitedPages.Count + 1)));
                var pageDir = Path.GetDirectoryName(target) ?? reportRoot;
                var page = PageModel.Read(parser.ParseDocument(DetectEncoding(bytes).GetString(bytes)),
                    DiskExplorerHtmlPath.RelativeToRoot(reportRoot, target));
                if (page.TopItems.Count == 0)
                {
                    return;
                }

                builder.MergePrimary(node, page.TopItems[0], target, cancellationToken);
                var pageRef = DiskExplorerHtmlPath.RelativeToRoot(reportRoot, target);
                VisitOwnPage(page.TopItems[0], node, pageDir, target, depth + 1, cancellationToken);
                VisitChildLinks(page.TopItems[0], node, pageDir, target, depth + 1, cancellationToken);
                VisitNested(page.TopItems[0], node, pageDir, cancellationToken);
                foreach (var top in page.TopItems.Skip(1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var child = builder.AddChild(top, node, DiskExplorerElementKind.Folder,
                        pageRef, pageDir, cancellationToken);
                    VisitOwnPage(top, child, pageDir, target, depth + 1, cancellationToken);
                    VisitChildLinks(top, child, pageDir, target, depth + 1, cancellationToken);
                    VisitNested(top, child, pageDir, cancellationToken);
                }
            }
            finally
            {
                visiting.Remove(target);
            }
        }

        private static bool IsHtmlPath(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static byte[] ReadBounded(Stream source, long maxBytes, string what)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                throw new DiskExplorerHtmlMalformedDocumentException(
                    $"Report {what} exceeds the maximum supported size of {maxBytes} bytes.");
            }
        }

        if (buffer.Length == 0)
        {
            throw new DiskExplorerHtmlMalformedDocumentException($"Report {what} is empty.");
        }

        return buffer.ToArray();
    }

    internal static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        var declared = SniffMetaCharset(bytes)?.Trim().ToLowerInvariant();
        return declared switch
        {
            "windows-1252" or "cp1252" or "1252" or "iso-8859-1" or "latin1" => ResolveWindows1252(),
            "utf-8" or "utf8" or null or "" => Encoding.UTF8,
            _ => Encoding.UTF8
        };
    }

    private static Encoding ResolveWindows1252()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new DiskExplorerHtmlMalformedDocumentException(
                "Report declares Windows-1252 but the code page is unavailable on this platform.");
        }
    }

    private static string? SniffMetaCharset(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        var contentIndex = head.IndexOf("charset", StringComparison.OrdinalIgnoreCase);
        if (contentIndex < 0)
        {
            return null;
        }

        var equals = head.IndexOf('=', contentIndex);
        if (equals < 0)
        {
            return null;
        }

        var rest = head[(equals + 1)..].TrimStart(' ', '"', '\'');
        var end = rest.IndexOfAny([' ', '"', '\'', ';', '>']);
        return (end < 0 ? rest : rest[..end]).Trim();
    }
}



