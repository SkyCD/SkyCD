using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Localization;
using SmartFormat;
using YamlDotNet.Serialization;

namespace SkyCD.UI.Localization;

/// <summary>Namespaced YAML catalogs with parent-culture and English fallback.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly object gate = new();
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> sources = new(StringComparer.Ordinal);

    public static LocalizationService Current { get; } = new();

    public LocalizationService()
    {
        Register("core", new EmbeddedTranslationProvider());
    }

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Invalidate();
    }

    public string Get(string key, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (gate)
        {
            // Longest matching namespace lets reverse-domain plugin IDs coexist with core.
            var source = sources.Keys.Where(name => key.StartsWith(name + ".", StringComparison.Ordinal))
                .OrderByDescending(name => name.Length).FirstOrDefault();
            if (source is null)
            {
                return key;
            }

            var catalogs = sources[source];
            var localKey = key[(source.Length + 1)..];
            for (var candidate = culture ?? CurrentCulture; !string.IsNullOrEmpty(candidate.Name); candidate = candidate.Parent)
            {
                if (catalogs.TryGetValue(candidate.Name, out var values) && values.TryGetValue(localKey, out var value))
                {
                    return value;
                }
            }

            return catalogs.TryGetValue("en", out var fallback) && fallback.TryGetValue(localKey, out var english)
                ? english
                : key;
        }
    }

    public string Format(string key, params object[] arguments)
    {
        return Smart.Format(CurrentCulture, Get(key), arguments);
    }

    public void Register(string namespaceName, ITranslationProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentNullException.ThrowIfNull(provider);
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var deserializer = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
        foreach (var (cultureName, yaml) in provider.GetTranslations())
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var values = deserializer.Deserialize<Dictionary<string, string>>(yaml) ?? [];
            // A blank YAML value means untranslated; let the normal fallback resolve it.
            catalogs.Add(culture.Name, values.Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        lock (gate)
        {
            sources[namespaceName] = catalogs;
        }

        Invalidate();
    }

    public bool Unregister(string namespaceName)
    {
        bool removed;
        lock (gate)
        {
            removed = sources.Remove(namespaceName);
        }

        if (removed)
        {
            Invalidate();
        }

        return removed;
    }

    private void Invalidate()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        // Avalonia's indexer node observes the CLR property name; other consumers use Item[].
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private sealed class EmbeddedTranslationProvider : ITranslationProvider
    {
        public IReadOnlyDictionary<string, string> GetTranslations()
        {
            var assembly = typeof(LocalizationService).Assembly;
            const string prefix = "SkyCD.UI.Translations.";
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".yaml", StringComparison.Ordinal)))
            {
                var fileName = name[prefix.Length..^".yaml".Length];
                // The final dash separates the culture (including an optional region) from the catalog group.
                var separator = fileName.LastIndexOf('-');
                var culture = separator < 0 ? fileName : fileName[..separator];
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                translations[culture] = translations.GetValueOrDefault(culture, string.Empty) + "\n" + reader.ReadToEnd();
            }

            return translations;
        }
    }
}
