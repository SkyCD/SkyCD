namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Base plugin contract used by the host runtime.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the plugin display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the plugin semantic version.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Gets the minimum host version supported by this plugin.
    /// </summary>
    Version MinHostVersion { get; }

    /// <summary>
    /// Gets the optional maximum host version supported by this plugin.
    /// </summary>
    Version? MaxHostVersion => null;

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    string Description => string.Empty;

    /// <summary>
    /// Gets the plugin assembly file name when available.
    /// </summary>
    string FileName => Path.GetFileName(GetType().Assembly.Location);
}
