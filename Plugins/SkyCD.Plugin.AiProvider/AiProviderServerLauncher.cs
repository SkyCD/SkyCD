using System;
using System.Diagnostics;
using System.IO;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider;

/// <summary>Starts an allowlisted local provider server when its CLI is installed.</summary>
internal static class AiProviderServerLauncher
{
    public static string? Start(AiProviderSettings settings)
    {
        var provider = AiProviderCatalog.Find(settings.ProviderId);
        if (provider is null)
        {
            return "Select a supported provider.";
        }

        if (!provider.CanStartServer || AiProviderCatalog.ServerCommand(provider.Id) is not { } launch)
        {
            return $"{provider.Name} does not support starting its server from SkyCD.";
        }

        var executable = FindOnPath(launch.Command);
        if (executable is null)
        {
            return $"The '{launch.Command}' command was not found on PATH. Install {provider.Name} or start its server manually.";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = launch.Arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            });
            return process is null ? $"The {provider.Name} server could not be started." : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException
                                              or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The {provider.Name} server could not be started: {exception.Message}";
        }
    }

    private static string? FindOnPath(string command)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
