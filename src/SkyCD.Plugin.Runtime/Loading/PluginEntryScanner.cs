using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Finds plugin entry assemblies without executing them (issue #1229). Candidate DLLs are read
/// through a metadata-only context, so a stray or broken file below the plugin folder can never
/// run code during discovery; only assemblies that actually declare capabilities are loaded.
/// </summary>
public static class PluginEntryScanner
{
    private const string CapabilityInterfaceFullName = "SkyCD.SDK.Capabilities.IPluginCapability";

    /// <summary>
    /// Entry-assembly candidates below one discovery root. The enumeration still walks the tree
    /// (build outputs nest under <c>bin/</c>), but every file is metadata-read only; build-output
    /// and reference folders are excluded. Only capability-declaring assemblies are returned.
    /// </summary>
    public static IReadOnlyList<string> FindEntryAssemblyPaths(string directory, PathAssemblyResolver resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(resolver);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            return [];
        }

        using var metadataContext = new MetadataLoadContext(resolver);
        return files
            .Select(Path.GetFullPath)
            .Where(IsCandidatePluginAssemblyPath)
            .OrderBy(GetPriority)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path => DeclaresCapabilities(metadataContext, path))
            .ToList();
    }

    /// <summary>
    /// Files that failed even the metadata read (not valid .NET assemblies). Reported, never executed.
    /// </summary>
    public static IReadOnlyList<string> FindUnreadableAssemblyPaths(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            return [];
        }

        var unreadable = new List<string>();
        foreach (var file in files.Select(Path.GetFullPath).Where(IsCandidatePluginAssemblyPath))
        {
            try
            {
                _ = AssemblyName.GetAssemblyName(file);
            }
            catch (Exception)
            {
                unreadable.Add(file);
            }
        }

        return unreadable;
    }

    internal static bool DeclaresCapabilities(MetadataLoadContext metadataContext, string assemblyPath)
    {
        System.Reflection.Assembly metadataAssembly;
        try
        {
            metadataAssembly = metadataContext.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception)
        {
            return false;
        }

        Type[] types;
        try
        {
            types = metadataAssembly.GetTypes();
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            // A single unresolved constructor or interface must not abort the directory scan.
            // MetadataLoadContext throws when a referenced type is missing; that file is not an entry.
            return types.Any(static type =>
                !type.IsAbstract &&
                type.GetInterfaces().Any(static face =>
                    string.Equals(face.FullName, CapabilityInterfaceFullName, StringComparison.Ordinal)) &&
                type.GetConstructors().Any(static constructor => constructor.GetParameters().Length == 0));
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool IsCandidatePluginAssemblyPath(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.Equals(fileName, "SkyCD.SDK.dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var refSegment = $"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}";
        var refIntSegment = $"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}";

        return !normalized.Contains(objSegment, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(refSegment, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(refIntSegment, StringComparison.OrdinalIgnoreCase);
    }

    internal static int GetPriority(string fullPath)
    {
        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var releaseSegment =
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}";
        var debugSegment =
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}";

        if (normalized.Contains(releaseSegment, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (normalized.Contains(debugSegment, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
