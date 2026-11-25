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

    // Mirrors src/SkyCD.App/App.axaml so templated controls realize their item containers and
    // DynamicResource lookups see the same SimpleTheme, design tokens and style dictionaries.
    private sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            var tokens = new Uri("avares://SkyCD.App/Styles/DesignTokens.axaml");
            Resources.MergedDictionaries.Add(new ResourceInclude(tokens) { Source = tokens });
            Styles.Add(new SimpleTheme());
            foreach (var style in new[]
                     {
                         "GlobalStyles",
                         "TabControlStyles",
                         "IconStyles",
                         "ButtonStyles",
                         "TreeViewStyles",
                         "MenuStyles",
                     })
            {
                var uri = new Uri($"avares://SkyCD.App/Styles/{style}.axaml");
                Styles.Add(new StyleInclude(uri) { Source = uri });
            }
        }
    }
}
