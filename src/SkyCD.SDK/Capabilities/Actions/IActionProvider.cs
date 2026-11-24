using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Actions;

/// <summary>Provides item-specific actions, including installation and launch.</summary>
public interface IActionProvider : IPluginCapability
{
    /// <summary>Gets actions available for the supplied item.</summary>
    IEnumerable<SkyAction> GetActions(Item item);
}
