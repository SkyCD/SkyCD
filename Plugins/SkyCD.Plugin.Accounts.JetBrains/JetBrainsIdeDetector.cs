using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.Accounts.JetBrains;

/// <summary>Finds JetBrains IDEs installed through Toolbox, standalone installers or application folders.</summary>
public sealed class JetBrainsIdeDetector
{
    private const int MaxDepth = 8;

    private readonly IReadOnlyList<string> roots;
    private readonly bool isWindows;
    private readonly bool isMacOS;

    public JetBrainsIdeDetector()
        : this(DefaultRoots(), OperatingSystem.IsWindows(), OperatingSystem.IsMacOS())
    {
    }

    public JetBrainsIdeDetector(IEnumerable<string> roots, bool isWindows, bool isMacOS)
    {
        this.roots = (roots ?? []).Where(static root => !string.IsNullOrWhiteSpace(root)).ToArray();
        this.isWindows = isWindows;
        this.isMacOS = isMacOS;
    }

    public IReadOnlyList<JetBrainsIdeInstall> Detect()
    {
        var installs = new List<JetBrainsIdeInstall>();
        foreach (var root in roots)
        {
            if (isMacOS)
            {
                ScanMacApplications(root, installs);
            }
            else
            {
                ScanDirectoryTree(root, installs);
            }
        }

        return installs
            .GroupBy(install => install.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(install => install.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(install => install.ExecutablePath.Length)
                .First())
            .OrderBy(install => install.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ScanMacApplications(string root, List<JetBrainsIdeInstall> installs)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var app in Directory.EnumerateDirectories(root, "*.app", SearchOption.TopDirectoryOnly))
            {
                var executableDirectory = Path.Combine(app, "Contents", "MacOS");
                if (!Directory.Exists(executableDirectory))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(executableDirectory))
                {
                    if (JetBrainsProducts.TryGetProductCodeForExecutable(Path.GetFileName(file), out var code))
                    {
                        installs.Add(CreateInstall(code, file));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void ScanDirectoryTree(string root, List<JetBrainsIdeInstall> installs)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (path, depth) = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    if (JetBrainsProducts.TryGetProductCodeForExecutable(Path.GetFileName(file), out var code))
                    {
                        installs.Add(CreateInstall(code, file));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(path))
                {
                    pending.Push((directory, depth + 1));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static JetBrainsIdeInstall CreateInstall(string productCode, string executablePath)
    {
        return new JetBrainsIdeInstall
        {
            ProductCode = productCode,
            ProductName = JetBrainsProducts.ResolveName(productCode),
            ExecutablePath = executablePath,
            Version = ExtractVersion(executablePath)
        };
    }

    private static string? ExtractVersion(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var match = Regex.Match(Path.GetFileName(directory), @"\b\d{2,4}\.\d+(?:\.\d+)*\b");
            if (match.Success)
            {
                return match.Value;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static IReadOnlyList<string> DefaultRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Add(Path.Combine(local, "Programs"));
            Add(Path.Combine(local, "JetBrains", "Toolbox", "apps"));
            Add(Path.Combine(roaming, "JetBrains", "Toolbox", "apps"));
            Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JetBrains"));
            Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "JetBrains"));
            return roots;

            void Add(string? path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    roots.Add(path);
                }
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            roots.Add("/Applications");
            if (!string.IsNullOrWhiteSpace(home))
            {
                roots.Add(Path.Combine(home, "Applications"));
            }

            return roots;
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            roots.Add(Path.Combine(home, ".local", "share", "JetBrains", "Toolbox", "apps"));
            roots.Add(Path.Combine(home, "Applications"));
        }

        roots.Add("/opt");
        return roots;
    }
}
