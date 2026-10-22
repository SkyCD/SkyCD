using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class PluginTranslationRegistryTests
{
    [Fact]
    public void Refresh_LoadsLatePluginUsingAlreadySelectedCulture()
    {
        var service = new LocalizationService();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            service.SetCulture(CultureInfo.GetCultureInfo("lt-LT"));
            var registry = CreateRegistry(service);

            registry.Refresh([Plugin("sample.reader", new Dictionary<string, string>
            {
                ["en"] = "title: Reader",
                ["lt"] = "title: Skaityklė"
            })]);

            Assert.Equal("Skaityklė", service.Get("plugins.sample.reader.title"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    [Fact]
    public void Refresh_MalformedPluginDoesNotPreventValidPluginLoading()
    {
        var service = new LocalizationService();
        var registry = CreateRegistry(service);

        registry.Refresh([
            Plugin("broken", new Dictionary<string, string> { ["en"] = "title: First\ntitle: Duplicate" }),
            Plugin("valid", new Dictionary<string, string> { ["en"] = "title: Valid" })
        ]);

        Assert.Equal("plugins.broken.title", service.Get("plugins.broken.title"));
        Assert.Equal("Valid", service.Get("plugins.valid.title", CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void Refresh_MalformedReplacementKeepsLastGoodCatalog()
    {
        var service = new LocalizationService();
        var registry = CreateRegistry(service);
        registry.Refresh([Plugin("sample", new Dictionary<string, string> { ["en"] = "title: Original" })]);

        registry.Refresh([Plugin("sample", new Dictionary<string, string> { ["en"] = "title: First\ntitle: Duplicate" })]);

        Assert.Equal("Original", service.Get("plugins.sample.title", CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void Refresh_RemovesCatalogForUnloadedPluginAndPreservesActivePlugin()
    {
        var service = new LocalizationService();
        var registry = CreateRegistry(service);
        var retained = Plugin("retained", new Dictionary<string, string> { ["en"] = "title: Retained" });
        registry.Refresh([
            Plugin("removed", new Dictionary<string, string> { ["en"] = "title: Removed" }),
            retained
        ]);

        registry.Refresh([retained]);

        Assert.Equal("plugins.removed.title", service.Get("plugins.removed.title"));
        Assert.Equal("Retained", service.Get("plugins.retained.title", CultureInfo.GetCultureInfo("en")));
    }

    private static PluginTranslationRegistry CreateRegistry(LocalizationService service) =>
        new(service, NullLogger<PluginTranslationRegistry>.Instance);

    private static DiscoveredPlugin Plugin(string id, IReadOnlyDictionary<string, string> translations) => new()
    {
        Id = id,
        Name = id,
        Version = new Version(1, 0),
        MinHostVersion = new Version(1, 0),
        FileName = id + ".dll",
        Capabilities = [new TranslationCapability(translations)]
    };

    private sealed class TranslationCapability(IReadOnlyDictionary<string, string> translations) : ILocalizationPluginCapability
    {
        public IReadOnlyDictionary<string, string> GetTranslations() => translations;
    }
}
