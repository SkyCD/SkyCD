using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Runtime wrapper for a loaded plugin and discovered capabilities.
/// </summary>
public sealed class DiscoveredPlugin
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public PluginAuthorDocument? Author { get; init; }

    public string? ProjectUrl { get; init; }

    public required Version Version { get; init; }

    public required Version MinHostVersion { get; init; }

    public Version? MaxHostVersion { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Primary format extension without a leading dot, used as the plugin's icon identity.</summary>
    public string? PrimaryExtension { get; init; }

    /// <summary>Icon declared by the plugin, or <see langword="null"/> when the host must use the kind fallback.</summary>
    public string? Icon { get; init; }

    /// <summary>Capability family that supplies the fallback icon when <see cref="Icon"/> is absent.</summary>
    public PluginIconKind IconKind { get; init; } = PluginIconKind.Feature;

    public required string FileName { get; init; }

    /// <summary>Full path of the discovered assembly, or empty when the host loaded it without a file location.</summary>
    public string AssemblyLocation { get; init; } = string.Empty;

    public required IReadOnlyCollection<IPluginCapability> Capabilities { get; init; }

    public void RegisterPluginServices(IRegistrator registrator)
    {
        ArgumentNullException.ThrowIfNull(registrator);

        foreach (var capability in Capabilities)
        {
            RegisterPluginService(registrator, capability.GetType(), capability);

            foreach (var interfaceType in capability.GetType()
                         .GetInterfaces()
                         .Where(static type => type != typeof(IPluginCapability))
                         .Where(static type => typeof(IPluginCapability).IsAssignableFrom(type)))
            {
                RegisterPluginService(registrator, interfaceType, capability);
            }
        }
    }

    public PluginDocument ToDocument()
    {
        return new PluginDocument
        {
            Id = Id,
            Name = Name,
            Author = Author,
            ProjectUrl = ProjectUrl,
            Version = Version.ToString(),
            Constraints = new PluginConstraintsDocument
            {
                MinHostVersion = MinHostVersion.ToString(),
                MaxHostVersion = MaxHostVersion?.ToString()
            },
            Description = Description,
            PrimaryExtension = PrimaryExtension,
            Icon = Icon,
            IconKind = IconKind.ToString(),
            AssemblyPath = FileName,
            IsEnabled = true,
            IsAvailable = true,
            LastDiscoveredAt = DateTimeOffset.UtcNow
        };
    }

    private static void RegisterPluginService(IRegistrator registrator, Type serviceType, IPluginCapability capability)
    {
        registrator.RegisterInstance(serviceType, capability,
            ifAlreadyRegistered: IfAlreadyRegistered.AppendNewImplementation);
        registrator.RegisterInstance(serviceType, capability, serviceKey: serviceType,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }
}
