using Avalonia;
using SkyCD.Cli;
using System;

namespace SkyCD.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var cliResult = CliEntryPoint.TryRun(args);
        if (cliResult.Handled)
        {
            Environment.ExitCode = cliResult.ExitCode;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
