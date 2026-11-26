using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkyCD.Plugin.Feature.GenDesc.Models;

/// <summary>
/// A user-editable template for one output kind. The header lines declare the kind, torrent category,
/// MIME types and extensions the template covers; the remaining text is the template body.
/// </summary>
public sealed record TemplateInfo(
    string Id,
    string Kind,
    string Category,
    string Language,
    string Site,
    string Version,
    string MimeTypes,
    string Extensions,
    string Body)
{
    public const string DefaultSite = "default";
    public const string HeaderPrefix = "## ";
    public const string NfoKind = "nfo";
    public const string DescriptionKind = "description";
    public const string GeneralCategory = "other";

    /// <summary>Parses template content into an instance, filling gaps from the supplied fallbacks.</summary>
    public static TemplateInfo Parse(
        string content,
        string fallbackId,
        string fallbackKind = DescriptionKind,
        string fallbackCategory = GeneralCategory,
        string fallbackLanguage = "en")
    {
        content ??= string.Empty;
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < lines.Length && lines[index].StartsWith(HeaderPrefix, StringComparison.Ordinal))
        {
            var pair = lines[index][HeaderPrefix.Length..];
            var split = pair.IndexOf('=');
            if (split > 0)
            {
                var key = pair[..split].Trim();
                var value = pair[(split + 1)..].Trim();
                if (key.Length > 0 && value.Length > 0) fields[key] = value;
            }
            index++;
        }

        var body = string.Join("\n", lines.Skip(index));
        while (body.StartsWith('\n')) body = body[1..];

        return new TemplateInfo(
            NormalizeKey(fields.GetValueOrDefault("id", fallbackId)),
            NormalizeKind(fields.GetValueOrDefault("kind", fallbackKind)),
            NormalizeKey(fields.GetValueOrDefault("category", fallbackCategory)),
            NormalizeKey(fields.GetValueOrDefault("lang", fields.GetValueOrDefault("language", fallbackLanguage))),
            NormalizeKey(fields.GetValueOrDefault("site", DefaultSite)),
            NormalizeKey(fields.GetValueOrDefault("version", "1")),
            NormalizeList(fields.GetValueOrDefault("mime", fields.GetValueOrDefault("mimetypes", string.Empty))),
            NormalizeExtensions(fields.GetValueOrDefault("ext", fields.GetValueOrDefault("extensions", string.Empty))),
            body);
    }

    /// <summary>Renders the header plus body for a seeded or exported template file.</summary>
    public string ToFileContent()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("## id=").Append(Id).Append('\n');
        builder.Append("## kind=").Append(Kind).Append('\n');
        builder.Append("## category=").Append(Category).Append('\n');
        if (Site.Length > 0) builder.Append("## site=").Append(Site).Append('\n');
        if (Language.Length > 0) builder.Append("## lang=").Append(Language).Append('\n');
        if (Version.Length > 0) builder.Append("## version=").Append(Version).Append('\n');
        if (MimeTypes.Length > 0) builder.Append("## mime=").Append(MimeTypes).Append('\n');
        if (Extensions.Length > 0) builder.Append("## ext=").Append(Extensions).Append('\n');
        builder.Append(Body);
        return builder.ToString();
    }

    /// <summary>MIME types covered by the template; empty means every type.</summary>
    public IReadOnlyList<string> MimeList => MimeTypes
        .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToArray();

    /// <summary>File extensions covered by the template, each with a leading dot.</summary>
    public IReadOnlyList<string> ExtensionList => Extensions
        .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
        .ToArray();

    public bool MatchesMime(string? mimeType)
    {
        if (MimeList.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(mimeType)) return false;
        var value = mimeType.Trim();
        foreach (var candidate in MimeList)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) return true;
            if (candidate.EndsWith("/*", StringComparison.Ordinal) &&
                value.StartsWith(candidate[..^1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool MatchesExtension(string? extension)
    {
        if (ExtensionList.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var value = extension.StartsWith('.') ? extension : "." + extension;
        return ExtensionList.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsNfo => string.Equals(Kind, NfoKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>A stable display name used by the options page and CLI listings.</summary>
    public string DisplayName => $"{Id} ({Category}, {Language})";

    public static string NormalizeKind(string? kind) =>
        string.Equals(kind?.Trim(), NfoKind, StringComparison.OrdinalIgnoreCase) ? NfoKind : DescriptionKind;

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizeList(string value) =>
        string.Join(' ', value
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeExtensions(string value) =>
        string.Join(' ', value
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.')
                ? extension.ToLower(CultureInfo.InvariantCulture)
                : "." + extension.ToLower(CultureInfo.InvariantCulture)));
}
