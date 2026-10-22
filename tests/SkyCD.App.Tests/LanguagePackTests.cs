using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Couchbase.Lite;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LanguagePackTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "skycd-i18n-" + Guid.NewGuid());
    private readonly CultureInfo culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    [Fact]
    public void Discovery_LoadsMetadataAndTranslationsFromDisk()
    {
        WritePack("fr", "_name: Français\n_flag: 🇫🇷\nui.Cancel: Annuler");
        var service = new LocalizationService(directory);

        var french = Assert.Single(service.GetAvailableLanguages(), language => language.CultureCode == "fr");
        Assert.Equal("Français", french.DisplayName);
        Assert.Equal("🇫🇷", french.Flag);
        service.SetLanguage("fr");
        Assert.Equal("Annuler", service.Get(UiKeys.Cancel));
        Assert.Equal("OK", service.Get(UiKeys.Ok));
    }

    [Fact]
    public void Discovery_SkipsMalformedAndEmptyPacksAndKeepsEnglishFallback()
    {
        WritePack("fr", "ui.Cancel: [invalid");
        WritePack("de", "_name: Deutsch");
        WritePack("lt", "ui.Cancel: first\nui.Cancel: duplicate");
        var service = new LocalizationService(directory);

        Assert.Equal("en", Assert.Single(service.GetAvailableLanguages()).CultureCode);
        service.SetLanguage("fr");
        Assert.Equal("en", service.CurrentCulture.Name);
        Assert.Equal("Cancel", service.Get(UiKeys.Cancel));
    }

    [Fact]
    public void MissingDirectory_FallsBackFromPersistedLanguage()
    {
        var service = new LocalizationService(directory);
        service.SetLanguage("Lithuanian");
        Assert.Equal("en", service.CurrentCulture.Name);
        Assert.Equal("en", Assert.Single(service.GetAvailableLanguages()).CultureCode);
    }

    [Fact]
    public void UserOverride_MergesWithInstalledPack()
    {
        WritePack("fr", "ui.Cancel: Annuler\nui.Ok: Accepter");
        var userDirectory = Path.Combine(directory, "user");
        Directory.CreateDirectory(Path.Combine(userDirectory, "fr"));
        File.WriteAllText(Path.Combine(userDirectory, "fr", "custom.yaml"), "ui.Cancel: Retour");
        var service = new LocalizationService(directory, userDirectory);
        service.SetLanguage("fr");

        Assert.Equal("Retour", service.Get(UiKeys.Cancel));
        Assert.Equal("Accepter", service.Get(UiKeys.Ok));
    }

    [Theory]
    [InlineData(false, "en")]
    [InlineData(true, "fr")]
    public void Options_AppliesImmediatelyAndRevertsOnlyOnCancel(bool accepted, string expectedCulture)
    {
        WritePack("fr", "ui.Cancel: Annuler");
        var service = new LocalizationService(directory);
        service.SetLanguage("en");
        var options = new OptionsDialogViewModel(service);

        options.SelectedLanguage = options.Languages.Single(language => language.CultureCode == "fr");
        Assert.Equal("Annuler", service.Get(UiKeys.Cancel));
        options.CompleteLanguageSelection(accepted);

        Assert.Equal(expectedCulture, service.CurrentCulture.Name);
        var settings = new AppOptionsDocument { Language = "en" };
        if (accepted)
        {
            settings.Language = options.SelectedLanguage.CultureCode;
        }

        var configuration = new DatabaseConfiguration { Directory = directory };
        using (var writer = new Database("options", configuration))
        {
            using var document = settings.ToMutableDocument(AppOptionsDocument.DocumentId);
            writer.GetDefaultCollection().Save(document);
        }

        using var reader = new Database("options", configuration);
        using var savedDocument = reader.GetDefaultCollection().GetDocument(AppOptionsDocument.DocumentId);
        var restoredSettings = savedDocument!.FromDocument<AppOptionsDocument>();
        var restarted = new LocalizationService(directory);
        restarted.SetLanguage(restoredSettings!.Language);
        Assert.Equal(expectedCulture, restarted.CurrentCulture.Name);
    }

    [Fact]
    public void Options_LanguageChangeKeepsLanguageCategoryOpen()
    {
        var service = LocalizationService.Current;
        var previous = service.CurrentCulture;
        try
        {
            service.SetLanguage("en");
            var options = new OptionsDialogViewModel { SelectedTabIndex = 1 };
            options.SelectedLanguage = options.Languages.Single(language => language.CultureCode == "lt");

            Assert.Equal(1, options.SelectedTabIndex);
            Assert.True(options.ShowLanguageSection);
            Assert.Equal("Atšaukti", service.Get(UiKeys.Cancel));
            options.CompleteLanguageSelection(false);
            Assert.Equal("Cancel", service.Get(UiKeys.Cancel));
        }
        finally
        {
            service.SetCulture(previous);
        }
    }

    private void WritePack(string code, string yaml)
    {
        var path = Path.Combine(directory, code);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "ui.yaml"), yaml);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
    }
}
