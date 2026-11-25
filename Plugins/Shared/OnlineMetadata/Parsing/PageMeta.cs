using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SkyCD.Plugin.OnlineMetadata.Models;

namespace SkyCD.Plugin.OnlineMetadata.Parsing;

/// <summary>
/// Extracts common media facts from arbitrary HTML using AngleSharp: Open Graph / description meta tags and,
/// most reliably, schema.org JSON-LD blocks that IMDb, AniDB, Anime News Network and TV.com all embed. It fills
/// a <see cref="DescriptionFacts"/> only where a value is found and never overwrites one, so a source-specific
/// source plugin can layer extra selectors on top. Parse errors leave the facts untouched rather than throwing.
/// </summary>
internal static class PageMeta
{
    private static readonly string[] PreferredTypes = ["movie", "tvseries", "tvseason", "tvepisode", "series", "book", "game"];
    private static readonly string[] SecondaryTypes = ["creativework", "article", "webpage"];
    private static readonly Regex YearPattern = new(@"(1[89]\d{2}|20\d{2}|21\d{2})", RegexOptions.Compiled);

    /// <summary>Parses HTML with AngleSharp, returning null for input the parser cannot turn into a document.</summary>
    public static IDocument? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        try
        {
            return new HtmlParser().ParseDocument(html);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Applies the shared meta / JSON-LD extraction to the supplied facts.</summary>
    public static void Apply(string html, DescriptionFacts facts)
    {
        var doc = Parse(html);
        if (doc is null) return;

        var ogTitle = Meta(doc, "og:title", "twitter:title");
        var ogDescription = Meta(doc, "og:description", "twitter:description", "description");
        facts.Set("Title", ogTitle ?? CleanTitle(doc.QuerySelector("title")?.TextContent));
        facts.Set("Plot", ogDescription);

        foreach (var node in EnumerateJsonLd(doc))
            ApplyJsonLd(node, facts);
    }

    private static string? Meta(IDocument doc, params string[] names)
    {
        foreach (var name in names)
        {
            var element = doc.QuerySelector($"meta[property=\"{name}\"]") ?? doc.QuerySelector($"meta[name=\"{name}\"]");
            var value = element?.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static IEnumerable<JsonElement> EnumerateJsonLd(IDocument doc)
    {
        foreach (var script in doc.QuerySelectorAll("script[type=\"application/ld+json\"]"))
        {
            var text = script.TextContent;
            if (string.IsNullOrWhiteSpace(text)) continue;
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(text);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
            foreach (var node in Flatten(root)) yield return node;
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var nested in Flatten(item)) yield return nested;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object) yield break;

        yield return element;
        if (element.TryGetProperty("@graph", out var graph))
            foreach (var nested in Flatten(graph)) yield return nested;
    }

    private static void ApplyJsonLd(JsonElement node, DescriptionFacts facts)
    {
        var types = GetStringArray(node, "@type");
        var rank = Rank(types);
        if (rank == 0) return;

        var name = GetString(node, "name");
        if (name is not null) facts.Set("Title", name);
        facts.Set("OriginalTitle", GetString(node, "alternateName"));
        facts.Set("Plot", GetString(node, "description"));
        facts.Set("Rating", AggregateRating(node));

        var genre = string.Join("|", GetStringArray(node, "genre"));
        facts.Set("Genre", genre.Length > 0 ? genre : null);

        var year = YearFrom(GetString(node, "datePublished") ?? GetString(node, "startDate") ?? GetString(node, "uploadDate"));
        facts.Set("Year", year);

        var duration = GetString(node, "duration");
        if (duration is not null) facts.Set("Runtime", Iso8601Duration(duration));

        var director = Names(node, "director");
        if (director.Length > 0) facts.Set("Director", director);
        var actors = Names(node, "actor");
        if (actors.Length > 0) facts.Set("Cast", actors);
        var studio = Names(node, "productionCompany");
        if (studio.Length > 0) facts.Set("Studio", studio);

        if (node.TryGetProperty("numberOfEpisodes", out var episodes) && episodes.ValueKind == JsonValueKind.Number)
            facts.Set("Episodes", episodes.GetRawText());
    }

    private static int Rank(IReadOnlyList<string> types)
    {
        foreach (var type in types)
        {
            if (PreferredTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) return 2;
            if (SecondaryTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) return 1;
        }
        return 0;
    }

    private static string? AggregateRating(JsonElement node)
    {
        if (!node.TryGetProperty("aggregateRating", out var rating) || rating.ValueKind != JsonValueKind.Object) return null;
        if (rating.TryGetProperty("ratingValue", out var value))
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (rating.TryGetProperty("bestRating", out var best) && !string.IsNullOrWhiteSpace(text))
            {
                var bestText = best.ValueKind == JsonValueKind.String ? best.GetString() : best.GetRawText();
                if (!string.IsNullOrWhiteSpace(bestText)) return $"{text}/{bestText}";
            }
            return text;
        }
        return null;
    }

    private static string Names(JsonElement node, string property) => string.Join("|", GetStringArray(node, property));

    /// <summary>
    /// Reads a property that may be a string, a number, an array of those, or an array/object of nodes with a
    /// "name", and returns every textual leaf. Media schema is inconsistent enough that all of these occur.
    /// </summary>
    private static IReadOnlyList<string> GetStringArray(JsonElement node, string property)
    {
        var results = new List<string>();
        if (node.TryGetProperty(property, out var value)) Collect(value, results);
        return results;

        static void Collect(JsonElement element, List<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    Add(element.GetString(), into);
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    Add(element.GetRawText(), into);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Collect(item, into);
                    break;
                case JsonValueKind.Object:
                    Add(GetString(element, "name"), into);
                    break;
            }
        }
    }

    private static void Add(string? value, List<string> into)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (!into.Contains(trimmed, StringComparer.Ordinal)) into.Add(trimmed);
    }

    private static string? GetString(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Object when value.TryGetProperty("@value", out var literal) => literal.GetString(),
            _ => null
        };
    }

    private static string? YearFrom(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var match = YearPattern.Match(date);
        return match.Success ? match.Value : null;
    }

    /// <summary>Converts an ISO-8601 duration such as <c>PT1H30M</c> to a compact "1 h 30 min" form.</summary>
    public static string? Iso8601Duration(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, @"^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$", RegexOptions.IgnoreCase);
        if (!match.Success) return value.Trim();
        var hours = int.Parse(match.Groups[2].Success ? match.Groups[2].Value : "0", CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[3].Success ? match.Groups[3].Value : "0", CultureInfo.InvariantCulture);
        var days = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : "0", CultureInfo.InvariantCulture);
        var totalMinutes = days * 1440 + hours * 60 + minutes;
        if (totalMinutes <= 0) return null;
        if (totalMinutes < 60) return $"{totalMinutes} min";
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var value = title.Trim();
        foreach (var suffix in new[] { " - IMDb", " | Wikipedia", " - Anime News Network", " - TV.com", " — TV.com" })
        {
            var index = value.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (index > 0) value = value[..index];
        }
        return value.Trim();
    }
}
