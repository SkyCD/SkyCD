using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkyCD.Plugin.Ai.GenDesc.Models;

/// <summary>
/// A user-editable description template. A leading block of <c>## key=value</c> header lines declares the
/// catalog site, language and version a template applies to; the remaining text is the template body handed
/// to the engine. Templates without a header default to the neutral site and language.
/// </summary>
public sealed record TemplateInfo(string Id, string Site, string Language, string Version, string Body)
{
    public const string DefaultSite = "default";
    public const string HeaderPrefix = "## ";

    /// <summary>
    /// Parses template content into an instance. <paramref name="fallbackId"/> and
    /// <paramref name="fallbackLanguage"/> fill gaps when the header omits them.
    /// </summary>
    public static TemplateInfo Parse(string content, string fallbackId, string fallbackLanguage = "en")
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

        var site = fields.GetValueOrDefault("site", DefaultSite);
        var language = fields.GetValueOrDefault("lang", fields.GetValueOrDefault("language", fallbackLanguage));
        var version = fields.GetValueOrDefault("version", "1");
        var id = fields.GetValueOrDefault("id", fallbackId);
        return new TemplateInfo(Normalize(id), Normalize(site), Normalize(language), Normalize(version), body);
    }

    /// <summary>Renders the header for a shipped or exported template body.</summary>
    public string ToFileContent() =>
        string.Concat(
            $"## id={Id}\n",
            $"## site={Site}\n",
            $"## lang={Language}\n",
            $"## version={Version}\n",
            Body);

    /// <summary>A stable composite key used by tests and diagnostics.</summary>
    public string QualifiedName => $"{Site}/{Language}@{Version}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Trim().ToLowerInvariant();
}
