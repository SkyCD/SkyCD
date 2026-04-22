using Avalonia;
using SkyCD.Cli;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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

        var cliResult = CliEntryPoint.TryRun(
            args,
            stdout: CliStdIo.CreateOutputWriter(),
            stderr: CliStdIo.CreateErrorWriter());
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

internal static class CliStdIo
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private static readonly TextWriter fallbackOut = TextWriter.Null;
    private static readonly TextWriter fallbackError = TextWriter.Null;

    public static void EnsureConsoleAttached()
    {
        if (HasUsableStdHandle(StandardHandle.Output) || HasUsableStdHandle(StandardHandle.Error))
        {
            return;
        }

        AttachConsole(AttachParentProcess);
    }

    public static TextWriter CreateOutputWriter() => CreateWriter(StandardHandle.Output, fallbackOut);

    public static TextWriter CreateErrorWriter() => CreateWriter(StandardHandle.Error, fallbackError);

    private static TextWriter CreateWriter(StandardHandle handle, TextWriter fallback)
    {
        var stdHandle = GetStdHandle((int)handle);
        if (stdHandle == IntPtr.Zero || stdHandle == InvalidHandleValue)
        {
            return fallback;
        }

        try
        {
            var safeHandle = new SafeFileHandle(stdHandle, ownsHandle: false);
            var stream = new FileStream(safeHandle, FileAccess.Write);
            return TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            });
        }
        catch
        {
            return fallback;
        }
    }

    private static bool HasUsableStdHandle(StandardHandle handle)
    {
        var stdHandle = GetStdHandle((int)handle);
        return stdHandle != IntPtr.Zero && stdHandle != InvalidHandleValue;
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private enum StandardHandle
    {
        Output = -11,
        Error = -12
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
}
