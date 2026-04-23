namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Base plugin contract used by the host runtime.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Gets immutable plugin metadata.
    /// </summary>
    PluginDescriptor Descriptor { get; }
}
