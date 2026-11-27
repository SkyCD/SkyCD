using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

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
    /// The token aborts a walk that a newer scan superseded.
    /// </summary>
    public static IReadOnlyList<string> FindEntryAssemblyPaths(
        string directory,
        PathAssemblyResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(resolver);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = EnumerateDllFiles(directory, cancellationToken);

        using var metadataContext = new MetadataLoadContext(resolver);
        var entries = new List<string>();
        foreach (var path in files
                     .Select(Path.GetFullPath)
                     .Where(IsCandidatePluginAssemblyPath)
                     .OrderBy(GetPriority)
                     .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeclaresCapabilities(metadataContext, path))
            {
                entries.Add(path);
            }
        }

        return entries;
    }

    /// <summary>
    /// Files that failed even the metadata read (not valid .NET assemblies). Reported, never executed.
    /// </summary>
    public static IReadOnlyList<string> FindUnreadableAssemblyPaths(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = EnumerateDllFiles(directory, cancellationToken);

        var unreadable = new List<string>();
        foreach (var file in files.Select(Path.GetFullPath).Where(IsCandidatePluginAssemblyPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static IReadOnlyList<string> EnumerateDllFiles(string directory, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(file);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An unreadable subtree leaves the folder without candidates, as before.
            return [];
        }

        return files;
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
               !normalized.Contains(refIntSegment, StringComparison.OrdinalIgnoreCase) &&
               !IsOrphanedBuildOutput(normalized);
    }

    /// <summary>
    /// Build output belongs to the project folder that holds its <c>bin</c>. When that project is gone, the
    /// leftover output would keep resurrecting a removed plugin, so it is skipped. Deployed plugin folders
    /// hold their DLLs without a <c>bin</c> segment and stay unaffected.
    /// </summary>
    private static bool IsOrphanedBuildOutput(string normalizedPath)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar);
        var binIndex = Array.FindLastIndex(segments, segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase));
        if (binIndex <= 0)
        {
            return false;
        }

        var projectDirectory = string.Join(Path.DirectorySeparatorChar, segments, 0, binIndex);
        try
        {
            return !Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            return true;
        }
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
