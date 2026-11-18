using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>Finds classic Office applications and the Click-to-Run package installed on Windows.</summary>
public sealed class Microsoft365OfficeDetector
{
    private static readonly string[] Executables =
    [
        "WINWORD.EXE",
        "EXCEL.EXE",
        "POWERPNT.EXE",
        "OUTLOOK.EXE",
        "MSACCESS.EXE",
        "ONENOTE.EXE",
        "MSPUB.EXE",
        "VISIO.EXE",
        "WINPROJ.EXE"
    ];

    private readonly Func<IReadOnlyDictionary<string, string>> readAppPaths;
    private readonly Func<Microsoft365OfficePackage?> readPackage;
    private readonly bool isWindows;

    public Microsoft365OfficeDetector()
        : this(ReadInstalledAppPaths, ReadClickToRunPackage, OperatingSystem.IsWindows())
    {
    }

    public Microsoft365OfficeDetector(
        Func<IReadOnlyDictionary<string, string>> readAppPaths,
        Func<Microsoft365OfficePackage?> readPackage,
        bool isWindows)
    {
        this.readAppPaths = readAppPaths ?? throw new ArgumentNullException(nameof(readAppPaths));
        this.readPackage = readPackage ?? throw new ArgumentNullException(nameof(readPackage));
        this.isWindows = isWindows;
    }

    public IReadOnlyList<OfficeAppInstall> Detect()
    {
        if (!isWindows)
        {
            return [];
        }

        var package = readPackage();
        var installs = new List<OfficeAppInstall>();
        foreach (var pair in readAppPaths())
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || !File.Exists(pair.Value))
            {
                continue;
            }

            if (!Microsoft365Products.TryResolveAppCode(pair.Key, out var productCode))
            {
                continue;
            }

            installs.Add(new OfficeAppInstall
            {
                ProductCode = productCode,
                ProductName = Microsoft365Products.ResolveAppName(productCode),
                ExecutablePath = pair.Value,
                Version = package?.Version,
                Suite = string.IsNullOrWhiteSpace(package?.ProductName) ? null : package.ProductName
            });
        }

        return installs
            .GroupBy(install => install.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(install => install.ExecutablePath.Length).First())
            .OrderBy(install => install.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadInstalledAppPaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return paths;
        }

        WindowsOfficeRegistry.ReadInstalledAppPaths(paths);
        return paths;
    }

    private static Microsoft365OfficePackage? ReadClickToRunPackage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return WindowsOfficeRegistry.ReadClickToRunPackage();
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsOfficeRegistry
    {
        public static void ReadInstalledAppPaths(Dictionary<string, string> paths)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var executable in Executables)
                {
                    var path = ReadAppPath(hive, executable);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths[executable] = path;
                    }
                }
            }
        }

        public static Microsoft365OfficePackage? ReadClickToRunPackage()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
                    if (key is null)
                    {
                        continue;
                    }

                    var releaseId = (key.GetValue("ProductReleaseIds") as string)?
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault();
                    return new Microsoft365OfficePackage
                    {
                        ReleaseId = releaseId ?? string.Empty,
                        ProductName = Microsoft365Products.ResolvePackageName(releaseId),
                        Version = key.GetValue("VersionToReport") as string,
                        Platform = key.GetValue("Platform") as string
                    };
                }
                catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException
                                                      or IOException)
                {
                    // Ignore inaccessible registry views and try the next one.
                }
            }

            return null;
        }

        private static string? ReadAppPath(RegistryHive hive, string executable)
        {
            var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executable}";
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey);
                    if (key?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim().Trim('"');
                    }
                }
                catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException
                                                      or IOException)
                {
                    // Ignore inaccessible registry views and try the next one.
                }
            }

            return null;
        }
    }
}
