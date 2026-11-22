using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SkyCD.Plugin.FileFormat.Vvv.Exceptions;

namespace SkyCD.Plugin.FileFormat.Vvv.Parsing;

internal sealed record VvvBackupEngineLayout(string RootPath, string ClientLibraryPath);

/// <summary>
/// Locates a Firebird 5 embedded engine that can be staged into a private
/// import workspace. The engine is never loaded from the plugin directory
/// itself because native libraries cannot be unloaded from the default
/// process; every import runs against a fresh copy.
/// </summary>
internal static class VvvBackupEngineLocator
{
    internal const string EnvironmentVariable = "SKYCD_FIREBIRD_ENGINE";

    private static readonly string[] ClientLibraryNames =
    [
        "fbclient.dll",
        "libfbclient.so.2",
        "libfbclient.so",
        "libfbclient.dylib"
    ];

    private static readonly string[] EnginePluginNames =
    [
        "engine13.dll",
        "libEngine13.so",
        "libEngine13.dylib"
    ];

    internal static VvvBackupEngineLayout Resolve()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
            if (File.Exists(configured))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(configured));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    candidates.Add(directory);
                }
            }
        }

        var assemblyDirectory = Path.GetDirectoryName(typeof(VvvBackupEngineLocator).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            candidates.Add(Path.Combine(assemblyDirectory, "firebird", "v5"));
            candidates.Add(Path.Combine(assemblyDirectory, "firebird", "v5", "engine"));
            candidates.Add(Path.Combine(assemblyDirectory, "firebird"));
            candidates.Add(assemblyDirectory);
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            candidates.Add(Path.Combine(baseDirectory, "firebird", "v5"));
            candidates.Add(Path.Combine(baseDirectory, "firebird", "v5", "engine"));
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localData))
        {
            candidates.Add(Path.Combine(localData, "SkyCD", "firebird", "v5"));
            candidates.Add(Path.Combine(localData, "SkyCD", "firebird", "v5", "engine"));
        }

        var runtime = RuntimeInformation.RuntimeIdentifier;
        var expanded = new List<string>();
        foreach (var candidate in candidates)
        {
            expanded.Add(candidate);
            expanded.Add(Path.Combine(candidate, ".firebird", runtime, "V5"));
            expanded.Add(Path.Combine(candidate, ".firebird", runtime, "V5", "engine"));
        }

        var searched = new List<string>();
        foreach (var candidate in expanded.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            searched.Add(candidate);
            var layout = TryResolve(candidate);
            if (layout is not null)
            {
                return layout;
            }
        }

        var summary = new StringBuilder();
        foreach (var candidate in searched)
        {
            summary.AppendLine();
            summary.Append(candidate);
        }

        throw new VvvBackupEngineUnavailableException(summary.ToString());
    }

    internal static VvvBackupEngineLayout? TryResolve(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(rootPath);
        if (!HasEnginePlugin(root))
        {
            return null;
        }

        foreach (var clientName in ClientLibraryNames)
        {
            var direct = Path.Combine(root, clientName);
            if (File.Exists(direct))
            {
                return new VvvBackupEngineLayout(root, direct);
            }

            var nested = Path.Combine(root, "lib", clientName);
            if (File.Exists(nested))
            {
                return new VvvBackupEngineLayout(root, nested);
            }
        }

        return null;
    }

    private static bool HasEnginePlugin(string root)
    {
        var plugins = Path.Combine(root, "plugins");
        return EnginePluginNames.Any(name => File.Exists(Path.Combine(plugins, name)));
    }
}
