using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>
/// Holds the templates available to a catalog: the built-in defaults plus any user templates on disk, keyed by
/// id so a user file with the same id overrides the built-in. Selection prefers an exact site and language
/// match and degrades toward the neutral English default so a request is never left without a template.
/// </summary>
public sealed class TemplateStore
{
    private readonly Dictionary<string, TemplateInfo> byId;

    public TemplateStore(IEnumerable<TemplateInfo> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        byId = new Dictionary<string, TemplateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates) byId[template.Id] = template;
    }

    /// <summary>All templates, built-in and user, ordered by id.</summary>
    public IReadOnlyList<TemplateInfo> Templates => byId.Values.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Builds a store from the built-ins overlaid with every <c>*.tpl</c> file in <paramref name="userDirectory"/>.
    /// A missing or unreadable directory is not an error: the built-in defaults remain selectable.
    /// </summary>
    public static TemplateStore Load(string? userDirectory)
    {
        var templates = new List<TemplateInfo>(BuiltInTemplates.All());
        if (!string.IsNullOrWhiteSpace(userDirectory) && Directory.Exists(userDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(userDirectory, "*.tpl"))
            {
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                var fallbackId = Path.GetFileNameWithoutExtension(file);
                templates.Add(TemplateInfo.Parse(content, fallbackId));
            }
        }
        return new TemplateStore(templates);
    }

    /// <summary>
    /// Writes each built-in template into <paramref name="userDirectory"/> only when a file of that id is not
    /// already present, so a user's edits survive reloads while a first run becomes editable on disk.
    /// </summary>
    public int SeedUserTemplates(string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        Directory.CreateDirectory(userDirectory);
        var written = 0;
        foreach (var template in Templates.Where(t => t.Id.StartsWith("default-", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(userDirectory, template.Id + ".tpl");
            if (File.Exists(path)) continue;
            File.WriteAllText(path, template.ToFileContent());
            written++;
        }
        return written;
    }

    /// <summary>
    /// Chooses the template for a catalog site and language. An explicit id wins outright; otherwise the best
    /// site/language combination is scored, and the neutral English default is the guaranteed fallback.
    /// </summary>
    public TemplateInfo? Select(string site, string language, string? templateId = null)
    {
        if (!string.IsNullOrWhiteSpace(templateId) && byId.TryGetValue(templateId, out var pinned)) return pinned;

        var siteKey = string.IsNullOrWhiteSpace(site) ? TemplateInfo.DefaultSite : site.Trim().ToLowerInvariant();
        var langKey = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

        TemplateInfo? best = null;
        var bestScore = int.MinValue;
        foreach (var template in Templates)
        {
            var score = Score(template, siteKey, langKey);
            if (score > bestScore)
            {
                bestScore = score;
                best = template;
            }
        }
        return best;
    }

    private static int Score(TemplateInfo template, string site, string language)
    {
        var languageScore = template.Language == language ? 100 : template.Language == "en" ? 10 : 0;
        var siteScore = template.Site == site ? 50 : template.Site == TemplateInfo.DefaultSite ? 5 : -20;
        return languageScore + siteScore;
    }
}
