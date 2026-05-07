using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class LanguageItemTests
{
    [Fact]
    public void Create_KnownLanguage_IncludesExpectedFlagInDisplayText()
    {
        var language = LanguageItem.Create("Lithuanian");

        Assert.Equal("🇱🇹", language.Flag);
        Assert.Equal("🇱🇹 Lithuanian", language.DisplayText);
    }

    [Fact]
    public void Create_LanguageVariant_ResolvesFlagFromBaseName()
    {
        var language = LanguageItem.Create("English (US)");

        Assert.Equal("🇬🇧", language.Flag);
        Assert.Equal("🇬🇧 English (US)", language.DisplayText);
    }

    [Fact]
    public void Create_UnknownLanguage_UsesFallbackFlag()
    {
        var language = LanguageItem.Create("Klingon");

        Assert.Equal("🌐", language.Flag);
        Assert.Equal("🌐 Klingon", language.DisplayText);
    }
}
