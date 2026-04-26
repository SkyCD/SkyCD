using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace SkyCD.Plugin.Abstractions.Localization;

public sealed class I18nService : II18nService
{
    private static readonly IStringLocalizer Localizer = BuildLocalizer();

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var localized = Localizer[key];
        return localized.ResourceNotFound ? key : localized.Value;
    }

    public string Format(string key, params object[] args)
    {
        if (args.Length == 0)
        {
            return Get(key);
        }

        var localized = Localizer[key, args];
        if (!localized.ResourceNotFound)
        {
            return localized.Value;
        }

        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    private static IStringLocalizer BuildLocalizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return factory.Create(typeof(Strings));
    }
}
