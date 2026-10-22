using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.UI.Localization;
using Xunit;
using YamlDotNet.Core;

namespace SkyCD.App.Tests;

public class LocalizationTests : IDisposable
{
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
    }

    [Fact]
    public void Constructor_LoadsEmbeddedCoreCatalog()
    {
        var service = new LocalizationService();

        Assert.Equal("Cancel", service.Get(UiKeys.Cancel, CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void TranslateExtension_BindsToSharedServiceIndexer()
    {
        var extension = new TranslateExtension(UiKeys.Cancel);

        var binding = Assert.IsType<Binding>(extension.ProvideValue(null!));

        Assert.Same(LocalizationService.Current, binding.Source);
        Assert.Equal($"[{UiKeys.Cancel}]", binding.Path);
        Assert.Equal(BindingMode.OneWay, binding.Mode);
    }

    [Fact]
    public void Get_FallsBackFromRegionToParentThenEnglish()
    {
        var service = new LocalizationService();
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "greeting: Hello\nenglishOnly: English fallback",
            ["fr"] = "greeting: Bonjour",
            ["fr-CA"] = "regional: Allô"
        }));

        Assert.Equal("Allô", service.Get("sample.regional", CultureInfo.GetCultureInfo("fr-CA")));
        Assert.Equal("Bonjour", service.Get("sample.greeting", CultureInfo.GetCultureInfo("fr-CA")));
        Assert.Equal("English fallback", service.Get("sample.englishOnly", CultureInfo.GetCultureInfo("fr-CA")));
        Assert.Equal("Hello", service.Get("sample.greeting", CultureInfo.GetCultureInfo("ja-JP")));
        Assert.Equal("sample.missing", service.Get("sample.missing"));
    }

    [Fact]
    public void SetCulture_UpdatesLookupAndNotifiesBindings()
    {
        var service = new LocalizationService();
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "greeting: Hello",
            ["fr"] = "greeting: Bonjour"
        }));
        var properties = new List<string?>();
        service.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal("fr", service.CurrentCulture.Name);
        Assert.Equal("Bonjour", service.Get("sample.greeting"));
        Assert.Contains(nameof(LocalizationService.CurrentCulture), properties);
        Assert.Contains("Item[]", properties);
    }

    [Fact]
    public void Register_AfterCultureSelectionMakesTranslationsAvailableImmediately()
    {
        var service = new LocalizationService();
        service.SetCulture(CultureInfo.GetCultureInfo("fr-CA"));
        Assert.Equal("sample.title", service.Get("sample.title"));

        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "title: Title",
            ["fr"] = "title: Titre"
        }));

        Assert.Equal("Titre", service.Get("sample.title"));
    }

    [Fact]
    public void ReplaceAndUnregister_OnlyAffectTheRegisteredNamespace()
    {
        var service = new LocalizationService();
        service.Register("first", new TranslationProvider(new Dictionary<string, string> { ["en"] = "title: First\nold: Old" }));
        service.Register("second", new TranslationProvider(new Dictionary<string, string> { ["en"] = "title: Second" }));
        service.Register("first", new TranslationProvider(new Dictionary<string, string> { ["en"] = "title: Replacement" }));

        Assert.Equal("Replacement", service.Get("first.title", CultureInfo.GetCultureInfo("en")));
        Assert.Equal("first.old", service.Get("first.old"));
        Assert.Equal("Second", service.Get("second.title", CultureInfo.GetCultureInfo("en")));

        service.Unregister("first");

        Assert.Equal("first.title", service.Get("first.title"));
        Assert.Equal("Second", service.Get("second.title", CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void Format_UsesSmartFormatPluralRules()
    {
        var service = new LocalizationService();
        service.SetCulture(CultureInfo.GetCultureInfo("en"));
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "count: '{0} {0:plural:item|items}'"
        }));

        Assert.Equal("1 item", service.Format("sample.count", 1));
        Assert.Equal("2 items", service.Format("sample.count", 2));
    }

    [Fact]
    public void Format_UsesSelectedCultureForNumbers()
    {
        var service = new LocalizationService();
        var culture = CultureInfo.GetCultureInfo("lt-LT");
        service.SetCulture(culture);
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "amount: '{0:N2}'"
        }));
        const decimal amount = 1234.5m;

        var formatted = service.Format("sample.amount", amount);

        Assert.Equal(amount.ToString("N2", culture), formatted);
        Assert.Contains(",50", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_DuplicateYamlKeysLeaveExistingCatalogIntact()
    {
        var service = new LocalizationService();
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "title: Original"
        }));

        Assert.ThrowsAny<YamlException>(() => service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "title: Replacement",
            ["lt"] = "title: First\ntitle: Duplicate"
        })));

        Assert.Equal("Original", service.Get("sample.title", CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Original", service.Get("sample.title", CultureInfo.GetCultureInfo("lt")));
    }

    [Fact]
    public void Get_NullYamlValueFallsBackToEnglish()
    {
        var service = new LocalizationService();
        service.Register("sample", new TranslationProvider(new Dictionary<string, string>
        {
            ["en"] = "title: English title",
            ["lt"] = "title: null"
        }));

        Assert.Equal("English title", service.Get("sample.title", CultureInfo.GetCultureInfo("lt")));
    }

    private sealed class TranslationProvider(IReadOnlyDictionary<string, string> translations) : ITranslationProvider
    {
        public IReadOnlyDictionary<string, string> GetTranslations() => translations;
    }
}
