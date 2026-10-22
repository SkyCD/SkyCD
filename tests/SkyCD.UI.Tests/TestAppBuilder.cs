using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(SkyCD.UI.Tests.TestAppBuilder))]

namespace SkyCD.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
