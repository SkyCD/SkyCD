using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

/// <summary>
/// Host facade for the single provider-neutral AI description capability. The optional capability stays
/// absent when the plugin is disabled, so stored descriptions remain readable without a provider.
/// </summary>
public sealed class DescriptionExtensionManager
{
    public DescriptionExtensionManager(IEnumerable<IGeneratedDescriptionCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Capability = capabilities.FirstOrDefault();
    }

    public IGeneratedDescriptionCapability? Capability { get; }

    public bool IsAvailable => Capability is not null;
}
