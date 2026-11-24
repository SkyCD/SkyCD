using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Reads label/value pairs from the definition lists and tables that anime and TV encyclopedia infoboxes are
/// built from. Labels are normalized (lower-cased, non-alphanumerics collapsed) so importers can match them
/// without depending on exact capitalization or trailing colons.
/// </summary>
internal static class Infobox
{
    public static IReadOnlyDictionary<string, string> Read(IDocument doc)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in doc.QuerySelectorAll("dl"))
        {
            string? label = null;
            foreach (var child in list.Children)
            {
                var tag = child.TagName.ToLowerInvariant();
                if (tag == "dt") label = Clean(child.TextContent);
                else if (tag == "dd" && label is not null)
                {
                    Add(values, label, child.TextContent);
                    label = null;
                }
            }
        }

        foreach (var row in doc.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("th, td").ToList();
            if (cells.Count < 2) continue;
            Add(values, Clean(cells[0].TextContent), cells[^1].TextContent);
        }

        return values;
    }

    /// <summary>Finds the first value whose label contains any of the supplied terms.</summary>
    public static string? Find(IReadOnlyDictionary<string, string> values, params string[] terms)
    {
        foreach (var term in terms)
        {
            var match = values.FirstOrDefault(pair => pair.Key.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Value)) return match.Value;
        }
        return null;
    }

    private static void Add(Dictionary<string, string> values, string label, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        var value = rawValue?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        var key = Normalize(label);
        if (key.Length == 0) return;
        if (!values.ContainsKey(key)) values[key] = value.Length > 1000 ? value[..1000] : value;
    }

    private static string Clean(string text) => text.Trim().TrimEnd(':');

    private static string Normalize(string label) =>
        new(label.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
}
