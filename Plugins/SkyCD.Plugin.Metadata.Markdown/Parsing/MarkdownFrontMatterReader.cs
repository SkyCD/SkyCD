using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SkyCD.Plugin.Metadata.Markdown.Parsing;

/// <summary>
/// Separates a YAML front matter block from the Markdown body and reads its top-level scalar values.
/// </summary>
/// <remarks>
/// A block only counts as front matter when both delimiters are present; an opening <c>---</c> with no
/// closing delimiter is a thematic break in Markdown, so the whole source stays in the body. Damaged YAML
/// does not fail the extraction: the body is still catalogued, and the invalid block is reported through
/// <see cref="Valid"/> instead of disappearing silently.
/// </remarks>
internal static class MarkdownFrontMatterReader
{
    private const int MaxItemsPerField = 32;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public static FrontMatter Read(string text, int maxFrontMatterCharacters, int maxValueCharacters)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var firstLineEnd = normalized.IndexOf('\n');
        var firstLine = (firstLineEnd < 0 ? normalized : normalized[..firstLineEnd]).Trim();
        if (!string.Equals(firstLine, "---", StringComparison.Ordinal))
        {
            return new FrontMatter(Present: false, Valid: false, normalized, NoValues);
        }

        var blockStart = firstLineEnd < 0 ? normalized.Length : firstLineEnd + 1;
        var lineStart = blockStart;
        while (lineStart <= normalized.Length)
        {
            var lineEnd = normalized.IndexOf('\n', lineStart);
            var lineLength = (lineEnd < 0 ? normalized.Length : lineEnd) - lineStart;
            var line = normalized.Substring(lineStart, lineLength).Trim();
            if (string.Equals(line, "---", StringComparison.Ordinal) ||
                string.Equals(line, "...", StringComparison.Ordinal))
            {
                var body = lineEnd < 0 ? string.Empty : normalized[(lineEnd + 1)..];
                var block = normalized[blockStart..lineStart];
                return block.Length > maxFrontMatterCharacters
                    ? new FrontMatter(Present: true, Valid: false, body, NoValues)
                    : Parse(block, body, maxValueCharacters);
            }

            if (lineEnd < 0)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return new FrontMatter(Present: false, Valid: false, normalized, NoValues);
    }

    private static FrontMatter Parse(string block, string body, int maxValueCharacters)
    {
        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(block));
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException)
        {
            return new FrontMatter(Present: true, Valid: false, body, NoValues);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is null)
        {
            return new FrontMatter(Present: true, Valid: true, body, NoValues);
        }

        if (yaml.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            return new FrontMatter(Present: true, Valid: false, body, NoValues);
        }

        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode { Value: { } rawKey })
            {
                continue;
            }

            var key = rawKey.Trim().ToLowerInvariant();
            if (key.Length == 0 || values.ContainsKey(key))
            {
                continue;
            }

            var fieldValues = ReadValues(entry.Value, maxValueCharacters);
            if (fieldValues.Count > 0)
            {
                values[key] = fieldValues;
            }
        }

        return new FrontMatter(Present: true, Valid: true, body, values);
    }

    private static IReadOnlyList<string> ReadValues(YamlNode node, int maxValueCharacters)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                var value = ReadScalar(scalar, maxValueCharacters);
                return value is null ? [] : [value];

            case YamlSequenceNode sequence:
                var values = new List<string>();
                foreach (var child in sequence.Children)
                {
                    if (values.Count >= MaxItemsPerField)
                    {
                        break;
                    }

                    if (child is YamlScalarNode item && ReadScalar(item, maxValueCharacters) is { } text)
                    {
                        values.Add(text);
                    }
                }

                return values;

            default:
                // A nested mapping has no single scalar meaning; it is reported as "no value" rather than
                // flattened into an ambiguous string.
                return [];
        }
    }

    private static string? ReadScalar(YamlScalarNode scalar, int maxValueCharacters)
    {
        var value = scalar.Value?.Trim();
        if (string.IsNullOrEmpty(value) ||
            string.Equals(value, "~", StringComparison.Ordinal) ||
            string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Length > maxValueCharacters ? value[..maxValueCharacters] : value;
    }
}

/// <summary>Front matter delimiters, validity and the top-level values keyed by lowercase name.</summary>
internal sealed record FrontMatter(
    bool Present,
    bool Valid,
    string Body,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values)
{
    /// <summary>Joins the first present key's values; later keys are only consulted when the first is absent.</summary>
    public string? First(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Values.TryGetValue(key, out var values) && values.Count > 0)
            {
                return string.Join(", ", values);
            }
        }

        return null;
    }
}
