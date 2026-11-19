using SkyCD.App.Converters;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class LanguageItemTests
{
    [Theory]
    [InlineData("🇬🇧", "flag-united-kingdom")]
    [InlineData("🇱🇹", "flag-lithuania")]
    [InlineData("🇿🇿", "globe-with-meridians")]
    [InlineData("🌐", "globe-with-meridians")]
    [InlineData("", "globe-with-meridians")]
    [InlineData(null, "globe-with-meridians")]
    public void ResolveAssetName_UsesFlagSequenceOrFallback(string? flag, string assetName)
    {
        Assert.Equal(assetName, LanguageFlagImageConverter.ResolveAssetName(flag));
    }

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
