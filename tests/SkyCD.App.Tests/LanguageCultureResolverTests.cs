using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class LanguageCultureResolverTests
{
    [Fact]
    public void ResolveCulture_KnownLanguageName_ReturnsExpectedCulture()
    {
        var culture = LanguageCultureResolver.ResolveCulture("Lithuanian");

        Assert.Equal("lt-LT", culture.Name);
    }

    [Fact]
    public void ResolveCulture_CultureCode_ReturnsRequestedCulture()
    {
        var culture = LanguageCultureResolver.ResolveCulture("en-GB");

        Assert.Equal("en-GB", culture.Name);
    }

    [Fact]
    public void ResolveCulture_UnknownLanguage_FallsBackToEnglishUs()
    {
        var culture = LanguageCultureResolver.ResolveCulture("Klingon");

        Assert.Equal("en-US", culture.Name);
    }
}
