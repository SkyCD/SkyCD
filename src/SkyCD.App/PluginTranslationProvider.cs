using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Localization;

namespace SkyCD.App;

internal sealed class PluginTranslationProvider(IEnumerable<ITranslationProvider> providers) : ITranslationProvider
{
    public IReadOnlyDictionary<string, string> GetTranslations()
    {
        var catalogs = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            foreach (var (culture, yaml) in provider.GetTranslations())
            {
                catalogs[culture] = catalogs.GetValueOrDefault(culture, string.Empty) + "\n" + yaml;
            }
        }

        return catalogs;
    }
}
