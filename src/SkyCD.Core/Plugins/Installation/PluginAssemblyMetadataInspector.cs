using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.PackagePaths;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Reads plugin identity from assembly metadata with <see cref="MetadataReader"/>: managed assemblies are opened
/// as PE files and their custom assembly attributes are decoded, so no plugin type is loaded, instantiated or executed.
/// </summary>
internal static class PluginAssemblyMetadataInspector
{
    /// <summary>Reads identity from one assembly; returns <c>null</c> for native or non-managed files.</summary>
    public static PluginAssemblyIdentitySnapshot? TryReadIdentity(string fullPath, string packagePath)
    {
        var attributes = PluginAssemblyAttributes.TryRead(fullPath);
        if (attributes is null)
        {
            return null;
        }

        var normalizedVersion = NormalizeAssemblyVersion(attributes.AssemblyVersion);
        return new PluginAssemblyIdentitySnapshot
        {
            PluginId = attributes.PluginId ?? attributes.AssemblyName,
            AssemblyName = attributes.AssemblyName,
            AssemblyVersion = normalizedVersion,
            MinHostVersion = attributes.MinHostVersion,
            MaxHostVersion = attributes.MaxHostVersion,
            EntryAssemblyPath = PackageEntryPath.TryNormalizeSafe(packagePath, out var normalizedPath,
                PackageEntryPath.PackageRules)
                ? normalizedPath
                : packagePath
        };
    }

    /// <summary>Reads every plugin identity contained in the extracted package content.</summary>
    public static IReadOnlyList<PluginAssemblyIdentitySnapshot> ReadAllIdentities(string contentDirectory)
    {
        var identities = new List<PluginAssemblyIdentitySnapshot>();
        foreach (var path in Directory.EnumerateFiles(contentDirectory, "*.dll", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(contentDirectory, path);
            try
            {
                var identity = TryReadIdentity(path, relativePath);
                if (identity is not null)
                {
                    identities.Add(identity);
                }
            }
            catch (BadImageFormatException)
            {
                // A native dependency is not a plugin assembly.
            }
            catch (IOException)
            {
                // Unreadable dependencies are ignored; the entry assembly is validated separately.
            }
        }

        return identities;
    }

    public static PluginAssemblyIdentitySnapshot ReadEntryIdentity(string packageId, string contentDirectory,
        string entryAssemblyPath)
    {
        if (!PackageEntryPath.TryResolveUnderRoot(contentDirectory, entryAssemblyPath, out var fullPath) ||
            !File.Exists(fullPath))
        {
            throw new PluginPackageAssemblyException(packageId,
                $"the entry assembly '{entryAssemblyPath}' is missing");
        }

        try
        {
            return TryReadIdentity(fullPath, entryAssemblyPath)
                   ?? throw new PluginPackageAssemblyException(packageId,
                       $"the entry assembly '{entryAssemblyPath}' is not a managed .NET assembly");
        }
        catch (BadImageFormatException)
        {
            throw new PluginPackageAssemblyException(packageId,
                $"the entry assembly '{entryAssemblyPath}' is not a managed .NET assembly");
        }
        catch (IOException)
        {
            throw new PluginPackageAssemblyException(packageId,
                $"the entry assembly '{entryAssemblyPath}' could not be read");
        }
    }

    /// <summary>Validates a candidate entry assembly path below the package content root.</summary>
    public static bool IsSafeRelativeAssemblyPath(string? path) =>
        PackageEntryPath.TryNormalizeSafe(path, out var normalized, PackageEntryPath.PackageRules) &&
        normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAssemblyVersion(Version version)
    {
        var major = Math.Max(version.Major, 0);
        var minor = Math.Max(version.Minor, 0);
        if (version.Build < 0)
        {
            return new Version(major, minor).ToString();
        }

        var build = version.Build;
        return version.Revision < 0
            ? new Version(major, minor, build).ToString()
            : new Version(major, minor, build, version.Revision).ToString();
    }
}
