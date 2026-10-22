using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Localization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SkyCD.UI.Localization;

public sealed partial class LocalizationService
{
    private readonly Dictionary<string, LanguagePack> languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("en", "English", "🇬🇧")
    };

    public IReadOnlyList<LanguagePack> GetAvailableLanguages() => languages.Values
        .OrderBy(language => language.CultureCode == "en" ? 0 : 1)
        .ThenBy(language => language.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public void SetLanguage(string? language)
    {
        var match = GetAvailableLanguages().FirstOrDefault(pack =>
            string.Equals(pack.CultureCode, language, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pack.DisplayName, language, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CultureInfo.GetCultureInfo(pack.CultureCode).EnglishName, language, StringComparison.OrdinalIgnoreCase));
        if (match is null && !string.IsNullOrWhiteSpace(language))
        {
            Trace.TraceWarning("Language pack '{0}' is unavailable; using English.", language);
        }

        SetCulture(CultureInfo.GetCultureInfo(match?.CultureCode ?? "en"));
    }

    private void LoadLanguagePacks(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var deserializer = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
            foreach (var path in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    var culture = CultureInfo.GetCultureInfo(Path.GetFileName(path));
                    if (string.IsNullOrEmpty(culture.Name))
                    {
                        continue;
                    }

                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var file in Directory.EnumerateFiles(path, "*.yaml").OrderBy(file => file, StringComparer.Ordinal))
                    {
                        var catalog = deserializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                        foreach (var pair in catalog ?? [])
                        {
                            values.Add(pair.Key, pair.Value);
                        }
                    }

                    if (!values.Any(pair => !pair.Key.StartsWith('_') && !string.IsNullOrWhiteSpace(pair.Value)))
                    {
                        continue;
                    }

                    languages.TryGetValue(culture.Name, out var previous);
                    var displayName = values.GetValueOrDefault("_name") ?? previous?.DisplayName ?? culture.EnglishName;
                    var flag = values.GetValueOrDefault("_flag") ?? previous?.Flag ?? "🌐";
                    lock (gate)
                    {
                        if (!sources["core"].TryGetValue(culture.Name, out var target))
                        {
                            target = new Dictionary<string, string>(StringComparer.Ordinal);
                            sources["core"][culture.Name] = target;
                        }

                        foreach (var pair in values.Where(pair => !pair.Key.StartsWith('_') && !string.IsNullOrWhiteSpace(pair.Value)))
                        {
                            target[pair.Key] = pair.Value;
                        }
                    }

                    languages[culture.Name] = new LanguagePack(culture.Name, displayName, flag);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlException or ArgumentException)
                {
                    Trace.TraceWarning("Ignoring invalid language pack '{0}': {1}", path, exception.Message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Cannot read language directory '{0}': {1}", directory, exception.Message);
        }
    }
}
