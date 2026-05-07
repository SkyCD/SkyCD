using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.Host.Menu;

/// <summary>
/// Host facade for menu contribution discovery and guarded command execution.
/// </summary>
public sealed class MenuExtensionManager(IEnumerable<IMenuPluginCapability> menuCapabilities)
{
    private readonly IReadOnlyList<(IMenuPluginCapability Capability, IReadOnlyCollection<MenuContribution> Contributions)> _bindings = menuCapabilities
        .Select(static capability => (capability, capability.GetMenuContributions()))
        .ToList();

    public IReadOnlyList<MenuContribution> GetMenuContributions(string? location = null)
    {
        var contributions = _bindings
            .SelectMany(binding => binding.Contributions)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(location))
        {
            contributions = contributions.Where(contribution =>
                contribution.Location.Equals(location, StringComparison.OrdinalIgnoreCase));
        }

        return contributions
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MenuCommandExecutionResult> ExecuteAsync(
        string commandId,
        MenuCommandContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var matchingCapability = ResolveCapability(commandId);

        if (matchingCapability is null)
        {
            return new MenuCommandExecutionResult
            {
                Success = false,
                Error = $"Command '{commandId}' was not found."
            };
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await matchingCapability.ExecuteMenuCommandAsync(commandId, context, linkedCts.Token);
            return new MenuCommandExecutionResult { Success = true };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new MenuCommandExecutionResult
            {
                Success = false,
                TimedOut = true,
                Error = $"Command '{commandId}' timed out."
            };
        }
        catch (Exception exception)
        {
            return new MenuCommandExecutionResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private IMenuPluginCapability? ResolveCapability(string commandId)
    {
        return _bindings
            .Where(binding => binding.Contributions.Any(contribution =>
                contribution.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase)))
            .Select(static binding => binding.Capability)
            .FirstOrDefault();
    }
}
