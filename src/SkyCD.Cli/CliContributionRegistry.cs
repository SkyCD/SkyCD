using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Cli;

internal sealed class CliContributionRegistry : IDisposable
{
    private static readonly StringComparer CommandComparer = StringComparer.OrdinalIgnoreCase;
    private readonly HashSet<string> commandPaths = new(CommandComparer);
    private readonly Dictionary<string, string> commandOwners = new(CommandComparer);
    private ServiceProvider? provider;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyCollection<string> CommandPaths => commandPaths.ToArray();

    public void Register(IEnumerable<DiscoveredPlugin> plugins)
    {
        provider?.Dispose();
        provider = null;
        commandPaths.Clear();
        commandOwners.Clear();

        var errors = new List<string>();
        var services = new ServiceCollection();
        var reservedCommands = CliHost.GetSystemCommandPaths();
        var registeredExtensionPoints = CliHost.GetExtensionPointPaths();

        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Capabilities.OfType<ICliPluginCapability>())
            {
                RegisterContribution(
                    services,
                    reservedCommands,
                    registeredExtensionPoints,
                    plugin,
                    capability,
                    capability.Command,
                    errors);
            }
        }

        Errors = errors;
        provider = services.BuildServiceProvider();
    }

    public RegisteredCliContribution? ResolveCommand(IReadOnlyList<string> args, out int consumedTokens)
    {
        consumedTokens = 0;
        if (provider is null)
        {
            return null;
        }

        for (var index = args.Count; index >= 1; index--)
        {
            var path = NormalizePath(args.Take(index));
            var contribution = provider.GetKeyedService<RegisteredCliContribution>(ToCommandServiceKey(path));
            if (contribution is null)
            {
                continue;
            }

            consumedTokens = index;
            return contribution;
        }

        return null;
    }

    public IReadOnlyList<RegisteredCliContribution> ResolveExtensions(string extensionPoint)
    {
        if (provider is null)
        {
            return [];
        }

        return provider
            .GetKeyedServices<RegisteredCliContribution>(ToExtensionServiceKey(extensionPoint))
            .OrderByDescending(static contribution => contribution.Contribution.Priority)
            .ThenBy(static contribution => contribution.Plugin.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        provider?.Dispose();
        provider = null;
    }

    private void RegisterContribution(
        IServiceCollection services,
        IReadOnlySet<string> reservedCommands,
        IReadOnlySet<string> extensionPoints,
        DiscoveredPlugin plugin,
        ICliPluginCapability capability,
        CliCommandContribution contribution,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(contribution.CommandPath) || string.IsNullOrWhiteSpace(contribution.CommandId))
        {
            errors.Add($"Plugin '{plugin.Id}' has CLI contribution with missing command path or command id.");
            return;
        }

        var normalizedPath = NormalizePath(contribution.CommandPath);
        var registration = new RegisteredCliContribution(plugin, capability, contribution);

        if (contribution.ContributionType == CliContributionType.Extension)
        {
            if (!extensionPoints.Contains(normalizedPath))
            {
                errors.Add(
                    $"Plugin '{plugin.Id}' contributes extension '{contribution.CommandPath}' but no such extension point exists.");
                return;
            }

            services.AddKeyedSingleton<RegisteredCliContribution>(ToExtensionServiceKey(normalizedPath), registration);
            return;
        }

        if (reservedCommands.Contains(normalizedPath))
        {
            errors.Add(
                $"Plugin '{plugin.Id}' cannot register command '{contribution.CommandPath}' because it is reserved by the host.");
            return;
        }

        if (!commandPaths.Add(normalizedPath))
        {
            var existingOwner = commandOwners.TryGetValue(normalizedPath, out var owner) ? owner : "unknown";
            errors.Add(
                $"CLI command collision on '{contribution.CommandPath}' between '{existingOwner}' and '{plugin.Id}'.");
            return;
        }

        commandOwners[normalizedPath] = plugin.Id;
        services.AddKeyedSingleton<RegisteredCliContribution>(ToCommandServiceKey(normalizedPath), registration);
    }

    private static string NormalizePath(string path)
    {
        return NormalizePath(path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string NormalizePath(IEnumerable<string> tokens)
    {
        return string.Join(' ', tokens).Trim();
    }

    private static string ToCommandServiceKey(string path)
    {
        return $"cmd::{NormalizePath(path).ToUpperInvariant()}";
    }

    private static string ToExtensionServiceKey(string path)
    {
        return $"ext::{NormalizePath(path).ToUpperInvariant()}";
    }

}

internal sealed record RegisteredCliContribution(
    DiscoveredPlugin Plugin,
    ICliPluginCapability Capability,
    CliCommandContribution Contribution);
