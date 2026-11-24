using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Imports media facts from Wikipedia through its public JSON APIs, which avoid HTML scraping entirely. A
/// direct <c>/wiki/Title</c> URL is resolved to the REST summary; otherwise the action API looks the title up
/// (following redirects) and returns a plain-text lead section. Only JSON is consumed, so parsing is stable.
/// </summary>
public sealed class WikipediaImporter : IWebSourceImporter
{
    public string SourceId => "wikipedia";

    public string? ResolveUrl(ImportRequest request)
    {
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim().ToLowerInvariant();
        if (UrlHelpers.MatchesHost(request.Url, "wikipedia.org"))
        {
            var title = WikipediaTitleFromUrl(request.Url!);
            if (!string.IsNullOrWhiteSpace(title))
                return $"https://{language}.wikipedia.org/api/rest_v1/page/summary/{UrlHelpers.Encode(title)}";
        }
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&redirects=1" +
                   $"&prop=extracts|pageimages&exintro&explaintext&piprop=thumbnail&pithumbsize=640&titles={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    public void Parse(string body, ImportRequest request, DescriptionFacts facts)
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

    private static string WikipediaTitleFromUrl(string url)
    {
        var path = new Uri(url, UriKind.Absolute).AbsolutePath;
        var marker = "/wiki/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;
        var tail = path[(index + marker.Length)..];
        var hash = tail.IndexOf('#');
        if (hash >= 0) tail = tail[..hash];
        return Uri.UnescapeDataString(tail.Replace('_', ' ')).Trim();
    }
}
