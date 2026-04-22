namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
///     Shared context object passed to plugin lifecycle methods.
/// </summary>
public sealed class PluginLifecycleContext
{
    /// <summary>
    ///     Gets the host version for compatibility checks.
    /// </summary>
    public required Version HostVersion { get; init; }

    /// <summary>
    ///     Gets the host service provider exposed to plugins.
    /// </summary>
    public IServiceProvider? Services { get; init; }
}