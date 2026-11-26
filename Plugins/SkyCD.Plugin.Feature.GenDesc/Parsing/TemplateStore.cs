using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Models;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// Holds the built-in templates plus every user template file on disk. A user file with the same id
/// overrides the built-in, and selection prefers a template that covers the entry's MIME type, extension
/// and category while keeping the requested language when one exists.
/// </summary>
public sealed class TemplateStore
{
    private readonly Dictionary<string, TemplateInfo> byId;

    public TemplateStore(IEnumerable<TemplateInfo> templates, string? directory = null, IEnumerable<string>? loadErrors = null)
    {
        ArgumentNullException.ThrowIfNull(templates);
        Directory = directory;
        LoadErrors = (loadErrors ?? []).ToArray();
        byId = new Dictionary<string, TemplateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            byId[template.Id] = template;
        }
    }

    /// <summary>The directory user templates are read from and saved to, or null for an in-memory store.</summary>
    public string? Directory { get; }

    /// <summary>Files that could not be read during <see cref="Load"/>; the built-ins remain usable.</summary>
    public IReadOnlyList<string> LoadErrors { get; }

    public IReadOnlyList<TemplateInfo> Templates => byId.Values
        .OrderBy(template => template.Kind, StringComparer.OrdinalIgnoreCase)
        .ThenBy(template => template.Category, StringComparer.OrdinalIgnoreCase)
        .ThenBy(template => template.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<TemplateInfo> ForKind(string kind) => Templates
        .Where(template => string.Equals(template.Kind, TemplateInfo.NormalizeKind(kind), StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public TemplateInfo? Find(string? id) =>
        !string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out var template) ? template : null;

    /// <summary>
    /// Loads the built-ins overlaid with every <c>*.tpl</c> file in <paramref name="userDirectory"/>. A
    /// missing directory is not an error; an unreadable file is reported and skipped.
    /// </summary>
    public static TemplateStore Load(string? userDirectory)
    {
        var templates = new List<TemplateInfo>(BuiltInTemplates.All());
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(userDirectory) && System.IO.Directory.Exists(userDirectory))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(userDirectory, "*.tpl").OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    templates.Add(TemplateInfo.Parse(content, Path.GetFileNameWithoutExtension(file)));
                }
                catch (TemplateSyntaxException exception)
                {
                    errors.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
            }
        }

        return new TemplateStore(templates, userDirectory, errors);
    }

    /// <summary>
    /// Writes each built-in template into <paramref name="userDirectory"/> only when a file of that id does
    /// not exist yet, so first-run templates become editable without overwriting user edits.
    /// </summary>
    public int SeedUserTemplates(string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        System.IO.Directory.CreateDirectory(userDirectory);
        var written = 0;
        foreach (var template in Templates.Where(template => template.Id.StartsWith("default-", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(userDirectory, template.Id + ".tpl");
            if (File.Exists(path)) continue;
            File.WriteAllText(path, template.ToFileContent());
            written++;
        }

        return written;
    }

    /// <summary>
    /// Chooses the template for an output kind. An explicit id wins when it matches the kind; otherwise the
    /// best MIME, extension, category and language combination is scored.
    /// </summary>
    public TemplateInfo? Select(
        string kind,
        string? mimeType,
        string? extension,
        string? category,
        string language,
        string? templateId = null)
    {
        var normalizedKind = TemplateInfo.NormalizeKind(kind);
        if (!string.IsNullOrWhiteSpace(templateId) && byId.TryGetValue(templateId, out var pinned) &&
            string.Equals(pinned.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase))
        {
            return pinned;
        }

        var categoryKey = string.IsNullOrWhiteSpace(category) ? TemplateInfo.GeneralCategory : category.Trim();
        var languageKey = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();

        TemplateInfo? best = null;
        var bestScore = int.MinValue;
        foreach (var template in Templates)
        {
            if (!string.Equals(template.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase)) continue;
            var score = Score(template, mimeType, extension, categoryKey, languageKey);
            if (score > bestScore)
            {
                bestScore = score;
                best = template;
            }
        }

        return best;
    }

    /// <summary>Writes a template as <c>&lt;id&gt;.tpl</c> and replaces the in-memory copy.</summary>
    public void Save(TemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(Directory))
        {
            throw new TemplateStorageException("the template directory is not configured");
        }

        var fileName = ValidateFileName(template.Id) + ".tpl";
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, fileName), template.ToFileContent());
        byId[template.Id] = template;
    }

    /// <summary>Deletes a user template file and removes the in-memory copy when it is not built in.</summary>
    public bool Delete(TemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(Directory))
        {
            throw new TemplateStorageException("the template directory is not configured");
        }

        var path = Path.Combine(Directory, ValidateFileName(template.Id) + ".tpl");
        var deleted = File.Exists(path);
        if (deleted)
        {
            File.Delete(path);
        }
        var builtIn = BuiltInTemplates.All().Any(candidate =>
            string.Equals(candidate.Id, template.Id, StringComparison.OrdinalIgnoreCase));
        if (!builtIn)
        {
            byId.Remove(template.Id);
        }
        else if (deleted)
        {
            var original = BuiltInTemplates.All().First(candidate =>
                string.Equals(candidate.Id, template.Id, StringComparison.OrdinalIgnoreCase));
            byId[template.Id] = original;
        }

        return deleted;
    }

    private static int Score(TemplateInfo template, string? mimeType, string? extension, string category, string language)
    {
        var score = 0;
        if (template.MimeList.Count == 0)
        {
            score += 80;
        }
        else if (template.MatchesMime(mimeType))
        {
            score += 400;
        }

        if (template.ExtensionList.Count == 0)
        {
            score += 60;
        }
        else if (template.MatchesExtension(extension))
        {
            score += 300;
        }

        if (string.Equals(template.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }
        else if (template.Category == TemplateInfo.GeneralCategory)
        {
            score += 20;
        }

        score += template.Language == language ? 100 : template.Language == "en" ? 10 : 0;
        score += template.Site == TemplateInfo.DefaultSite ? 5 : 0;
        return score;
    }

    private static string ValidateFileName(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new TemplateStorageException($"the template id '{id}' is not a valid file name");
        }

        return id;
    }
}
