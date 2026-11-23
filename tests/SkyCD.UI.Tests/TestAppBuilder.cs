using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Simple;

[assembly: AvaloniaTestApplication(typeof(SkyCD.UI.Tests.TestAppBuilder))]

namespace SkyCD.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    // Mirrors src/SkyCD.App/App.axaml so templated controls realize their item containers.
    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            var tokens = new Uri("avares://SkyCD.App/Styles/DesignTokens.axaml");
            var globalStyles = new Uri("avares://SkyCD.App/Styles/GlobalStyles.axaml");
            var tabControlStyles = new Uri("avares://SkyCD.App/Styles/TabControlStyles.axaml");
            Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
            Styles.Add(new SimpleTheme());
            Styles.Add(new StyleInclude(globalStyles) { Source = globalStyles });
            Styles.Add(new StyleInclude(tabControlStyles) { Source = tabControlStyles });
        }
    }
}
