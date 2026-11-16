using System;
using Avalonia;
using SkyCD.Cli;

namespace SkyCD.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            CliStdIo.EnsureConsoleAttached();
        }

        var cliResult = args.Length == 0 ? null : CliEntryPoint.TryRun(
            args,
            stdout: CliStdIo.CreateOutputWriter(),
            stderr: CliStdIo.CreateErrorWriter());
        if (cliResult is { Handled: true })
        {
            Environment.ExitCode = (int)cliResult.ExitCode;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
