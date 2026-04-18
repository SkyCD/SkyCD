namespace SkyCD.Plugin.Abstractions.Capabilities.Menu;

/// <summary>
/// Context passed to menu command execution.
/// </summary>
public sealed class MenuCommandContext
{
    /// <summary>
    /// Gets active catalog identifier.
    /// </summary>
    public Guid? ActiveCatalogId { get; init; }

    /// <summary>
    /// Gets selected node identifier.
    /// </summary>
    public long? SelectedNodeId { get; init; }

    /// <summary>
    /// Gets optional extension payload from host.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }

    /// <summary>
    /// Gets host actions that plugins are allowed to invoke.
    /// </summary>
    public IHostCommandApi? HostApi { get; init; }
}
