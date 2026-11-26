using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.SDK.Capabilities.GenDesc;

namespace SkyCD.Plugin.Feature.GenDesc.Rendering;

/// <summary>One rendered output plus the template and category that produced it.</summary>
public sealed record GenDescResult(string Text, TemplateInfo Template, string Category, string CategoryTitle);

/// <summary>Fills a template with the host facts and the locally computed category.</summary>
public static class GenDescRenderer
{
    public static GenDescResult Render(
        TemplateStore store,
        GenDescEntryContext context,
        string kind,
        string? language = null,
        string? templateId = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(context);

        var facts = new DescriptionFacts();
        foreach (var pair in context.Facts)
        {
            facts.Force(pair.Key, pair.Value);
        }

        var baseName = Path.GetFileNameWithoutExtension(context.FileName);
        if (baseName.Length == 0) baseName = context.FileName;
        facts.Force("Name", baseName);
        facts.Force("Title", facts.Has("Title") ? facts.Get("Title") : baseName);
        facts.Force("FileName", context.FileName);
        facts.Force("Extension", context.Extension);
        facts.Force("MimeType", context.MimeType);
        facts.Force("SizeBytes", facts.Has("SizeBytes") ? facts.Get("SizeBytes") : string.Empty);

        var category = GenDescCategory.Detect(context.Extension, context.MimeType, context.Facts);
        facts.Force("Category", category.Key);
        facts.Force("CategoryTitle", category.Title);

        var languageKey = string.IsNullOrWhiteSpace(language)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : language.Trim();
        var template = store.Select(kind, context.MimeType, context.Extension, category.Key, languageKey, templateId)
            ?? throw new GenDescGenerationException($"no {TemplateInfo.NormalizeKind(kind)} template is available");

        var text = TemplateEngine.Render(template.Body, facts);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new GenDescGenerationException(
                $"the template '{template.Id}' rendered no text for '{context.FileName}'");
        }

        return new GenDescResult(text, template, category.Key, category.Title);
    }

    /// <summary>Builds a context for a plain file path, used by the CLI where no catalog is open.</summary>
    public static async Task<GenDescEntryContext> FromFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var file = new FileInfo(filePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The file '{filePath}' does not exist.", filePath);
        }

        var extension = file.Extension.ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(file.Name);
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = baseName,
            ["Title"] = baseName,
            ["FileName"] = file.Name,
            ["File"] = file.Name,
            ["Extension"] = extension,
            ["MimeType"] = GenDescCategory.MimeFromExtension(extension),
            ["SizeBytes"] = file.Length.ToString(CultureInfo.InvariantCulture),
            ["Size"] = FormatSize(file.Length),
            ["Path"] = file.FullName,
            ["Directory"] = file.DirectoryName ?? string.Empty,
            ["LastWriteTime"] = file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        return new GenDescEntryContext(file.Name, extension, facts["MimeType"], facts);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
