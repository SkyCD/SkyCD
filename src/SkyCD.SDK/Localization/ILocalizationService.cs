using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;

namespace SkyCD.Plugin.Abstractions.Localization;

/// <summary>Shared culture, formatting, and dynamically registered translation sources.</summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Gets the culture used by lookups and formatting.</summary>
    CultureInfo CurrentCulture { get; }
    /// <summary>Gets languages discovered from host language packs, including emergency English.</summary>
    IReadOnlyList<LanguagePack> GetAvailableLanguages();
    /// <summary>Applies a discovered code or legacy display name, falling back to English.</summary>
    void SetLanguage(string? language);
    /// <summary>Resolves a namespaced key with parent-culture and English fallback.</summary>
    string Get(string key, CultureInfo? culture = null);
    /// <summary>Formats a translated message using the active culture.</summary>
    string Format(string key, params object[] arguments);
    /// <summary>Changes the culture and invalidates existing UI bindings.</summary>
    void SetCulture(CultureInfo culture);
    /// <summary>Atomically adds or replaces a namespace's YAML catalogs.</summary>
    void Register(string namespaceName, ITranslationProvider provider);
    /// <summary>Removes a namespace and invalidates its bindings.</summary>
    bool Unregister(string namespaceName);
}
