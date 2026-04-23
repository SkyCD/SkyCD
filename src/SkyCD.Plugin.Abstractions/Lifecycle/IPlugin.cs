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

    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    string Id => Descriptor.Id;

    /// <summary>
    /// Gets the plugin display name.
    /// </summary>
    string Name => Descriptor.DisplayName;

    /// <summary>
    /// Gets the plugin semantic version.
    /// </summary>
    Version Version => Descriptor.Version;

    /// <summary>
    /// Gets the minimum host version supported by this plugin.
    /// </summary>
    Version MinHostVersion => Descriptor.MinHostVersion;

    /// <summary>
    /// Gets the optional maximum host version supported by this plugin.
    /// </summary>
    Version? MaxHostVersion => Descriptor.MaxHostVersion;

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    string Description => Descriptor.Description ?? string.Empty;

    /// <summary>
    /// Gets the plugin assembly file name when available.
    /// </summary>
    string FileName => Path.GetFileName(GetType().Assembly.Location);
}
