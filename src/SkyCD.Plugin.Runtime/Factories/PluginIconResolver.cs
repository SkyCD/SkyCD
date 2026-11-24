using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Factories;

/// <summary>
/// Resolves the icon a plugin declares through <see cref="PluginIconAttribute"/> or a conventional
/// <c>icon.png</c>, and the capability kind that supplies a well-defined fallback when none is declared.
/// </summary>
internal static class PluginIconResolver
{
    internal const string ConventionalIconFileName = "icon.png";

    private const string FirstPartyAssemblyPrefix = "SkyCD.Plugin.";

    private static readonly string[] DirectIconPrefixes =
    [
        "lucide:", "ext:", "avares://", "data:image/", "base64:"
    ];

    public static string? ResolveDeclared(Assembly assembly)
    {
        var declared = assembly.GetCustomAttribute<PluginIconAttribute>()?.Icon;
        if (NormalizeDeclaredIcon(declared, assembly) is { } icon)
        {
            return icon;
        }

        var directory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var conventional = Path.Combine(directory, ConventionalIconFileName);
        return File.Exists(conventional) ? conventional : null;
    }

    public static PluginIconKind ResolveKind(string? assemblySimpleName,
        IReadOnlyList<IPluginCapability> capabilities)
    {
        return TryResolveKindFromAssemblyName(assemblySimpleName, out var named)
            ? named
            : ResolveKindFromCapabilities(capabilities);
    }

    /// <summary>
    /// First-party plugin assemblies carry their kind in the name (<c>SkyCD.Plugin.&lt;Kind&gt;.&lt;Subject&gt;</c>),
    /// which classifies plugin surfaces such as Accounts.Core that expose only generic CLI capabilities.
    /// </summary>
    internal static bool TryResolveKindFromAssemblyName(string? assemblySimpleName, out PluginIconKind kind)
    {
        kind = PluginIconKind.Feature;
        if (string.IsNullOrWhiteSpace(assemblySimpleName) ||
            !assemblySimpleName.StartsWith(FirstPartyAssemblyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = assemblySimpleName[FirstPartyAssemblyPrefix.Length..];
        var separator = remainder.IndexOf('.');
        var token = separator < 0 ? remainder : remainder[..separator];
        switch (token)
        {
            case "FileFormat":
                kind = PluginIconKind.FileFormat;
                return true;
            case "Metadata":
            case "Volume":
                kind = PluginIconKind.Metadata;
                return true;
            case "Preview":
                kind = PluginIconKind.Preview;
                return true;
            case "Accounts":
                kind = PluginIconKind.Accounts;
                return true;
            case "Ai":
                kind = PluginIconKind.Ai;
                return true;
            case "Feature":
            case "Config":
                kind = PluginIconKind.Feature;
                return true;
            default:
                return false;
        }
    }

    internal static PluginIconKind ResolveKindFromCapabilities(IReadOnlyList<IPluginCapability> capabilities)
    {
        if (capabilities.OfType<IFileFormatPluginCapability>().Any())
        {
            return PluginIconKind.FileFormat;
        }

        if (capabilities.OfType<IMediaMetadataCapability>().Any() ||
            capabilities.OfType<ILocalMediaMetadataCapability>().Any() ||
            capabilities.OfType<IAudioCdMetadataCapability>().Any() ||
            capabilities.OfType<IVolumeMetadataCapability>().Any())
        {
            return PluginIconKind.Metadata;
        }

        if (capabilities.OfType<IMediaPreviewCapability>().Any() ||
            capabilities.OfType<ILocalMediaPreviewCapability>().Any())
        {
            return PluginIconKind.Preview;
        }

        if (capabilities.OfType<IAccountConnector>().Any() ||
            capabilities.OfType<IAccountSyncProvider>().Any())
        {
            return PluginIconKind.Accounts;
        }

        if (capabilities.OfType<IGeneratedDescriptionCapability>().Any() ||
            capabilities.OfType<IAiDescriptionImplementationCapability>().Any() ||
            capabilities.OfType<IDescriptionGenerationPlanner>().Any() ||
            capabilities.OfType<IAiProviderSettingsCapability>().Any() ||
            capabilities.OfType<IAcpAgentSettingsCapability>().Any())
        {
            return PluginIconKind.Ai;
        }

        return PluginIconKind.Feature;
    }

    private static string? NormalizeDeclaredIcon(string? declared, Assembly assembly)
    {
        var value = declared?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DirectIconPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        var directory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.IsPathRooted(value) ? value : Path.Combine(directory, value);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }
}
