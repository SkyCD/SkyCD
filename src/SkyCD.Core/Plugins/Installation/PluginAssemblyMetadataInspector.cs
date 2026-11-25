using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SkyCD.PackagePaths;
using SkyCD.SDK.Lifecycle;
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
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return null;
        }

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            return null;
        }

        var definition = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(definition.Name);
        Version assemblyVersion;
        try
        {
            assemblyVersion = definition.Version;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        string? pluginId = null;
        string? minHostVersion = null;
        string? maxHostVersion = null;

        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            var attributeName = ResolveAttributeTypeName(reader, attribute);
            if (attributeName is null)
            {
                continue;
            }

            var argument = TryReadFirstStringArgument(reader, attribute);
            switch (attributeName)
            {
                case nameof(PluginIdAttribute) when !string.IsNullOrWhiteSpace(argument):
                    pluginId = argument.Trim();
                    break;
                case nameof(MinHostVersionAttribute) when !string.IsNullOrWhiteSpace(argument):
                    minHostVersion = argument.Trim();
                    break;
                case nameof(MaxHostVersionAttribute) when !string.IsNullOrWhiteSpace(argument):
                    maxHostVersion = argument.Trim();
                    break;
            }
        }

        var normalizedVersion = NormalizeAssemblyVersion(assemblyVersion);
        return new PluginAssemblyIdentitySnapshot
        {
            PluginId = pluginId ?? assemblyName,
            AssemblyName = assemblyName,
            AssemblyVersion = normalizedVersion,
            MinHostVersion = minHostVersion,
            MaxHostVersion = maxHostVersion,
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

    private static string? ResolveAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            var constructor = attribute.Constructor;
            EntityHandle typeHandle = constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                _ => default
            };

            return typeHandle.Kind switch
            {
                HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)typeHandle).Name),
                HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Name),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? TryReadFirstStringArgument(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.Length < 2 || blob.ReadUInt16() != 1)
        {
            return null;
        }

        try
        {
            return blob.ReadSerializedString();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

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
