using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Toml;
using OfficeIMO.Yaml;
using SkyCD.Plugin.Metadata.YamlToml.Models;

namespace SkyCD.Plugin.Metadata.YamlToml.Parsing;

/// <summary>
/// Parses one bounded buffer with OfficeIMO and collects structure plus a bounded page.
/// An invalid document yields an error position and no tree.
/// </summary>
internal static class YamlTomlDocumentReader
{
    public static YamlTomlFacts Read(
        byte[] bytes,
        string format,
        YamlTomlPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return format == "toml"
                ? ReadToml(stream, bytes.LongLength, options, cancellationToken)
                : ReadYaml(stream, bytes.LongLength, options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YamlException exception)
        {
            return Invalid(format, bytes.LongLength, exception.Code, exception.Line, exception.Column);
        }
        catch (TomlException exception)
        {
            return Invalid(format, bytes.LongLength, exception.Code, exception.Line, exception.Column);
        }
    }

    public static string ResolveFormat(string extension, string? contentType)
    {
        if (IsTomlContentType(contentType))
        {
            return "toml";
        }

        if (IsYamlContentType(contentType))
        {
            return "yaml";
        }

        return extension.Equals(".toml", StringComparison.OrdinalIgnoreCase) ? "toml" : "yaml";
    }

    private static YamlTomlFacts ReadYaml(
        Stream stream,
        long byteLength,
        YamlTomlPluginOptions options,
        CancellationToken cancellationToken)
    {
        using var document = YamlDocument.Load(stream, YamlLoadOptions.UntrustedDefaults, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var page = new PageBuilder(options);
        var mappingCount = 0;
        var sequenceCount = 0;
        var scalarCount = 0;
        var maxDepth = 0;
        var anchorCount = 0;
        var aliasCount = 0;
        var mergeKeyCount = 0;
        var commentCount = 0;
        string? version = null;
        var topLevel = new List<string>();

        foreach (var part in document.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(part.Version))
            {
                version = part.Version;
            }

            commentCount += CountComment(part.CommentBefore);
            commentCount += CountComment(part.Comment);
            commentCount += CountComment(part.CommentAfter);

            if (part.Root is null)
            {
                continue;
            }

            WalkYaml(part.Root, 1, string.Empty, page, ref mappingCount, ref sequenceCount, ref scalarCount,
                ref maxDepth, ref anchorCount, ref aliasCount, ref mergeKeyCount, ref commentCount, topLevel,
                options.MaxTopLevelNames, isTopLevel: true);
        }

        return new YamlTomlFacts
        {
            Format = "yaml",
            Valid = true,
            ByteLength = byteLength,
            FormatVersion = string.IsNullOrEmpty(version) ? null : version,
            DocumentCount = document.Documents.Count,
            MappingCount = mappingCount,
            SequenceCount = sequenceCount,
            ScalarCount = scalarCount,
            MaxDepth = maxDepth,
            AnchorCount = anchorCount,
            AliasCount = aliasCount,
            MergeKeyCount = mergeKeyCount,
            CommentCount = commentCount,
            DuplicateKeyCount = CountDuplicateKeys(document.Diagnostics),
            TopLevelKeys = topLevel,
            Lines = page.Lines
        };
    }

    private static void WalkYaml(
        YamlNode node,
        int depth,
        string indent,
        PageBuilder page,
        ref int mappingCount,
        ref int sequenceCount,
        ref int scalarCount,
        ref int maxDepth,
        ref int anchorCount,
        ref int aliasCount,
        ref int mergeKeyCount,
        ref int commentCount,
        List<string> topLevel,
        int maxTopLevel,
        bool isTopLevel)
    {
        if (depth > maxDepth)
        {
            maxDepth = depth;
        }

        if (!string.IsNullOrEmpty(node.Anchor))
        {
            anchorCount++;
        }

        page.AddComment(indent, node.CommentBefore);
        commentCount += CountComment(node.CommentBefore);
        commentCount += CountComment(node.Comment);

        if (node is YamlAlias alias)
        {
            aliasCount++;
            page.AddValueLine(indent, "*" + (alias.Name ?? string.Empty), node.Comment);
            return;
        }

        if (node is YamlMapping mapping)
        {
            mappingCount++;
            if (mapping.Count == 0 && !isTopLevel)
            {
                page.AddValueLine(indent, "{}", node.Comment);
                return;
            }

            foreach (var entry in mapping)
            {
                var keyText = NodeText(entry.Key);
                if (IsMergeKey(entry.Key, keyText))
                {
                    mergeKeyCount++;
                }

                if (isTopLevel && topLevel.Count < maxTopLevel && keyText.Length > 0)
                {
                    topLevel.Add(Trim(keyText, maxTopLevel * 4));
                }

                commentCount += CountComment(entry.Key.CommentBefore);
                commentCount += CountComment(entry.Key.Comment);
                page.AddComment(indent, entry.Key.CommentBefore);

                var value = entry.Value;
                if (value is YamlScalar || value is YamlAlias)
                {
                    CountNode(value, depth + 1, ref mappingCount, ref sequenceCount, ref scalarCount, ref maxDepth,
                        ref anchorCount, ref aliasCount, ref mergeKeyCount, ref commentCount);
                    page.AddKeyValue(indent, keyText, NodeText(value), value.Comment);
                }
                else
                {
                    page.AddKeyLine(indent, keyText, value.Comment);
                    WalkYaml(value, depth + 1, indent + "  ", page, ref mappingCount, ref sequenceCount,
                        ref scalarCount, ref maxDepth, ref anchorCount, ref aliasCount, ref mergeKeyCount,
                        ref commentCount, topLevel, maxTopLevel, isTopLevel: false);
                }

            }

            return;
        }

        if (node is YamlSequence sequence)
        {
            sequenceCount++;
            if (sequence.Count == 0)
            {
                page.AddValueLine(indent, "[]", node.Comment);
                return;
            }

            foreach (var item in sequence)
            {
                if (item is YamlScalar || item is YamlAlias)
                {
                    CountNode(item, depth + 1, ref mappingCount, ref sequenceCount, ref scalarCount, ref maxDepth,
                        ref anchorCount, ref aliasCount, ref mergeKeyCount, ref commentCount);
                    page.AddValueLine(indent + "- ", NodeText(item), item.Comment);
                }
                else
                {
                    page.AddValueLine(indent + "- ", string.Empty, null);
                    WalkYaml(item, depth + 1, indent + "  ", page, ref mappingCount, ref sequenceCount,
                        ref scalarCount, ref maxDepth, ref anchorCount, ref aliasCount, ref mergeKeyCount,
                        ref commentCount, topLevel, maxTopLevel, isTopLevel: false);
                }
            }

            return;
        }

        scalarCount++;
        page.AddValueLine(indent, NodeText(node), node.Comment);
    }

    private static void CountNode(
        YamlNode node,
        int depth,
        ref int mappingCount,
        ref int sequenceCount,
        ref int scalarCount,
        ref int maxDepth,
        ref int anchorCount,
        ref int aliasCount,
        ref int mergeKeyCount,
        ref int commentCount)
    {
        if (depth > maxDepth)
        {
            maxDepth = depth;
        }

        if (!string.IsNullOrEmpty(node.Anchor))
        {
            anchorCount++;
        }

        commentCount += CountComment(node.CommentBefore);
        commentCount += CountComment(node.Comment);

        if (node is YamlAlias)
        {
            aliasCount++;
            return;
        }

        if (node is YamlScalar)
        {
            scalarCount++;
        }
    }

    private static YamlTomlFacts ReadToml(
        Stream stream,
        long byteLength,
        YamlTomlPluginOptions options,
        CancellationToken cancellationToken)
    {
        using var document = TomlDocument.Load(stream, TomlLoadOptions.UntrustedDefaults);
        cancellationToken.ThrowIfCancellationRequested();

        var page = new PageBuilder(options);
        var tableCount = 0;
        var arrayOfTablesCount = 0;
        var keyCount = 0;
        var maxDepth = 0;
        var commentCount = 0;
        var topLevel = new List<string>();

        WalkToml(document.Root, 1, string.Empty, page, ref tableCount, ref arrayOfTablesCount, ref keyCount,
            ref maxDepth, ref commentCount, topLevel, options.MaxTopLevelNames, isRoot: true);

        return new YamlTomlFacts
        {
            Format = "toml",
            Valid = true,
            ByteLength = byteLength,
            MaxDepth = maxDepth,
            CommentCount = commentCount,
            DuplicateKeyCount = 0,
            TableCount = tableCount,
            ArrayOfTablesCount = arrayOfTablesCount,
            KeyCount = keyCount,
            TopLevelTables = topLevel,
            Lines = page.Lines
        };
    }

    private static void WalkToml(
        TomlNode? node,
        int depth,
        string indent,
        PageBuilder page,
        ref int tableCount,
        ref int arrayOfTablesCount,
        ref int keyCount,
        ref int maxDepth,
        ref int commentCount,
        List<string> topLevel,
        int maxTopLevel,
        bool isRoot)
    {
        if (node is null)
        {
            return;
        }

        if (depth > maxDepth)
        {
            maxDepth = depth;
        }

        if (node is TomlTable table)
        {
            if (!isRoot && table.TableKind != TomlTableKind.Root)
            {
                tableCount++;
            }

            foreach (var entry in table)
            {
                keyCount++;
                var child = entry.Value;
                commentCount += child.LeadingComments.Count;
                if (!string.IsNullOrEmpty(child.TrailingComment))
                {
                    commentCount++;
                }

                foreach (var leading in child.LeadingComments)
                {
                    page.AddCommentLine(indent, leading);
                }
                if (isRoot && topLevel.Count < maxTopLevel && child is TomlTable or TomlTableArray)
                {
                    topLevel.Add(Trim(entry.Key, maxTopLevel * 4));
                }

                if (child is TomlTable childTable && childTable.TableKind != TomlTableKind.Inline)
                {
                    page.AddKeyLine(indent, "[" + entry.Key + "]", child.TrailingComment);
                    WalkToml(child, depth + 1, indent + "  ", page, ref tableCount, ref arrayOfTablesCount,
                        ref keyCount, ref maxDepth, ref commentCount, topLevel, maxTopLevel, isRoot: false);
                }
                else if (child is TomlTableArray tableArray)
                {
                    arrayOfTablesCount++;
                    var itemIndex = 0;
                    foreach (var item in tableArray)
                    {
                        page.AddKeyLine(indent, "[[" + entry.Key + "]]", itemIndex == 0 ? child.TrailingComment : null);
                        WalkToml(item, depth + 1, indent + "  ", page, ref tableCount, ref arrayOfTablesCount,
                            ref keyCount, ref maxDepth, ref commentCount, topLevel, maxTopLevel, isRoot: false);
                        itemIndex++;
                    }
                }
                else
                {
                    page.AddKeyValue(indent, entry.Key, DescribeToml(child), child.TrailingComment);
                    if (child is TomlTable inline)
                    {
                        WalkToml(inline, depth + 1, indent + "  ", page, ref tableCount, ref arrayOfTablesCount,
                            ref keyCount, ref maxDepth, ref commentCount, topLevel, maxTopLevel, isRoot: false);
                    }
                }
            }

            return;
        }

        page.AddValueLine(indent, DescribeToml(node), node.TrailingComment);
    }

    private static string DescribeToml(TomlNode node)
    {
        if (node.Kind is TomlNodeKind.Array)
        {
            return node is TomlArray array ? "[" + array.Count + "]" : "[]";
        }

        if (node.Kind is TomlNodeKind.Table or TomlNodeKind.TableArray)
        {
            return string.Empty;
        }

        return node.AsString() ?? string.Empty;
    }

    private static YamlTomlFacts Invalid(string format, long byteLength, string? code, int line, int column)
    {
        return new YamlTomlFacts
        {
            Format = format,
            Valid = false,
            ByteLength = byteLength,
            ErrorCode = string.IsNullOrEmpty(code) ? "invalid" : code,
            ErrorLine = line > 0 ? line : null,
            ErrorColumn = column > 0 ? column : null
        };
    }

    private static int CountDuplicateKeys(IReadOnlyList<OfficeDiagnostic> diagnostics)
    {
        var count = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Code.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountComment(string? comment)
    {
        if (string.IsNullOrEmpty(comment))
        {
            return 0;
        }

        var count = 1;
        foreach (var ch in comment)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsMergeKey(YamlNode key, string text) =>
        (key is YamlScalar scalar && scalar.Kind == YamlScalarKind.Merge) || text == "<<";

    private static string NodeText(YamlNode node)
    {
        if (node is YamlAlias alias)
        {
            return "*" + (alias.Name ?? string.Empty);
        }

        return node.AsString() ?? string.Empty;
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static bool IsTomlContentType(string? contentType) =>
        Contains(contentType, "toml");

    private static bool IsYamlContentType(string? contentType) =>
        Contains(contentType, "yaml");

    private static bool Contains(string? contentType, string token) =>
        contentType is not null && contentType.Contains(token, StringComparison.OrdinalIgnoreCase);

    private sealed class PageBuilder
    {
        private readonly YamlTomlPluginOptions options;
        private readonly List<YamlTomlPreviewLine> lines = new();
        private int characters;

        public PageBuilder(YamlTomlPluginOptions options)
        {
            this.options = options;
        }

        public IReadOnlyList<YamlTomlPreviewLine> Lines => lines;

        public void AddComment(string indent, string? comment)
        {
            if (string.IsNullOrEmpty(comment))
            {
                return;
            }

            var normalized = comment.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                AddCommentLine(indent, line);
            }
        }

        public void AddCommentLine(string indent, string comment)
        {
            var text = comment.Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (!text.StartsWith('#'))
            {
                text = "# " + text;
            }

            Add(new YamlTomlPreviewLine(
            [
                new YamlTomlPreviewSpan(YamlTomlSpanKind.Value, indent),
                new YamlTomlPreviewSpan(YamlTomlSpanKind.Comment, text)
            ]));
        }

        public void AddKeyValue(string indent, string key, string value, string? trailingComment)
        {
            var spans = new List<YamlTomlPreviewSpan>
            {
                new(YamlTomlSpanKind.Value, indent),
                new(YamlTomlSpanKind.Key, key),
                new(YamlTomlSpanKind.Value, ": " + value)
            };
            AppendComment(spans, trailingComment);
            Add(new YamlTomlPreviewLine(spans));
        }

        public void AddKeyLine(string indent, string key, string? trailingComment)
        {
            var spans = new List<YamlTomlPreviewSpan>
            {
                new(YamlTomlSpanKind.Value, indent),
                new(YamlTomlSpanKind.Key, key)
            };
            AppendComment(spans, trailingComment);
            Add(new YamlTomlPreviewLine(spans));
        }

        public void AddValueLine(string indent, string value, string? trailingComment)
        {
            var spans = new List<YamlTomlPreviewSpan>
            {
                new(YamlTomlSpanKind.Value, indent + value)
            };
            AppendComment(spans, trailingComment);
            Add(new YamlTomlPreviewLine(spans));
        }

        private static void AppendComment(List<YamlTomlPreviewSpan> spans, string? trailingComment)
        {
            if (string.IsNullOrWhiteSpace(trailingComment))
            {
                return;
            }

            var text = trailingComment.Trim();
            if (!text.StartsWith('#'))
            {
                text = "# " + text;
            }

            spans.Add(new YamlTomlPreviewSpan(YamlTomlSpanKind.Comment, " " + text));
        }

        private void Add(YamlTomlPreviewLine line)
        {
            if (lines.Count >= options.MaxRenderedLines || characters >= options.MaxRenderedCharacters)
            {
                return;
            }

            var length = 0;
            foreach (var span in line.Spans)
            {
                length += span.Text.Length;
            }

            if (length == 0)
            {
                return;
            }

            var remaining = options.MaxRenderedCharacters - characters;
            if (length > remaining)
            {
                line = TrimLine(line, remaining);
                length = remaining;
            }

            if (line.Spans.Count == 0)
            {
                return;
            }

            lines.Add(line);
            characters += length;
        }

        private static YamlTomlPreviewLine TrimLine(YamlTomlPreviewLine line, int remaining)
        {
            var spans = new List<YamlTomlPreviewSpan>();
            foreach (var span in line.Spans)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (span.Text.Length <= remaining)
                {
                    spans.Add(span);
                    remaining -= span.Text.Length;
                    continue;
                }

                spans.Add(span with { Text = span.Text[..remaining] });
                remaining = 0;
            }

            return new YamlTomlPreviewLine(spans);
        }
    }
}
