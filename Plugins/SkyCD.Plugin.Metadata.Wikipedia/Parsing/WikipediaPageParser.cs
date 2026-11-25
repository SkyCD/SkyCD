using System;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.Metadata.Wikipedia.Parsing;

/// <summary>
/// Extracts title, summary and thumbnail facts from the two JSON shapes Wikipedia serves: the REST summary
/// and the action API query page. Malformed or unrelated content simply sets nothing.
/// </summary>
internal static class WikipediaPageParser
{
    public static void Parse(string body, DescriptionFacts facts)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.TryGetProperty("extract", out var summaryExtract))
        {
            facts.Set("Title", String(root, "title"));
            facts.Set("Genre", String(root, "description"));
            facts.Set("Plot", summaryExtract.ToString());
            if (root.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.Object)
                facts.Set("Image", String(thumb, "source"));
            return;
        }

        if (!root.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Object) return;

        var page = pages.EnumerateObject().FirstOrDefault();
        if (page.Value.ValueKind != JsonValueKind.Object) return;

        if (page.Value.TryGetProperty("title", out var title) &&
            !title.ToString().StartsWith("No title", StringComparison.OrdinalIgnoreCase))
            facts.Set("Title", title.ToString());
        if (page.Value.TryGetProperty("extract", out var extract))
            facts.Set("Plot", extract.ToString());
        if (page.Value.TryGetProperty("thumbnail", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.Object)
            facts.Set("Image", String(thumbnail, "source"));
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
