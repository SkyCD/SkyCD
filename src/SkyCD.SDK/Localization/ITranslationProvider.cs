using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Localization;

/// <summary>Supplies flat YAML catalogs keyed by BCP-47 culture name.</summary>
public interface ITranslationProvider
{
    /// <summary>Returns culture names mapped to flat YAML key/value documents.</summary>
    IReadOnlyDictionary<string, string> GetTranslations();
}
