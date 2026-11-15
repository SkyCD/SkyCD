using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
            Styles.Add(new SimpleTheme());
        }
    }
}
