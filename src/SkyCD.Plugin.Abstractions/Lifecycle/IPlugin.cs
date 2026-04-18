namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Base plugin contract used by the host runtime.
/// </summary>
public interface IPlugin : IAsyncDisposable
{
    /// <summary>
    /// Gets immutable plugin metadata.
    /// </summary>
    PluginDescriptor Descriptor { get; }

    /// <summary>
    /// Executes when the runtime loads the plugin assembly and creates an instance.
    /// </summary>
    ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes when host dependencies are available and plugin can initialize state.
    /// </summary>
    ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes when plugin becomes active and can serve runtime capabilities.
    /// </summary>
    ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default);
}
