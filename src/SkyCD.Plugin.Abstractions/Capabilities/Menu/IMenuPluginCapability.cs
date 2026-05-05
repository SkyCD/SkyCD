using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Menu;

/// <summary>
/// Capability contract for plugins that contribute host menu commands.
/// </summary>
public interface IMenuPluginCapability : IPluginCapability
{
    /// <summary>
    /// Gets declarative menu contributions.
    /// </summary>
    IReadOnlyCollection<MenuContribution> GetMenuContributions();

    /// <summary>
    /// Executes a contributed command.
    /// </summary>
    Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default);
}
