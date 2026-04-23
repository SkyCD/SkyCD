using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Cli;

internal sealed class CliContributionRegistry : IDisposable
{
    private static readonly StringComparer CommandComparer = StringComparer.OrdinalIgnoreCase;
    private const string CommandRegistryKey = "skycd.cli.command";
    private const string ExtensionPointRegistryKey = "skycd.cli.extension_point";
    private readonly HashSet<string> commandPaths = new(CommandComparer);
    private readonly Dictionary<string, string> commandOwners = new(CommandComparer);
    private readonly PluginDiscoveryService pluginDiscoveryService = new();
    private ServiceProvider? provider;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyCollection<string> CommandPaths => commandPaths.ToArray();

    public void Register(IEnumerable<DiscoveredPlugin> plugins)
    {
        provider?.Dispose();
        provider = null;
        commandPaths.Clear();
        commandOwners.Clear();

        var discoveredPlugins = pluginDiscoveryService.DiscoverFromPlugins(plugins.Select(static plugin => plugin.Plugin));
        var errors = new List<string>();
        var services = new ServiceCollection();
        RegisterHostCommandMetadata(services);
        using var hostMetadataProvider = services.BuildServiceProvider();
        var reservedCommands = hostMetadataProvider.GetKeyedServices<string>(CommandRegistryKey).ToArray();
        var registeredExtensionPoints = hostMetadataProvider.GetKeyedServices<string>(ExtensionPointRegistryKey).ToArray();

        foreach (var plugin in discoveredPlugins)
        {
            foreach (var capability in plugin.Capabilities.OfType<ICliPluginCapability>())
            {
                foreach (var contribution in capability.GetCliContributions())
                {
                    RegisterContribution(
                        services,
                        reservedCommands,
                        registeredExtensionPoints,
                        plugin,
                        capability,
                        contribution,
                        errors);
                }
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
            .ThenBy(static contribution => contribution.Plugin.Plugin.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        provider?.Dispose();
        provider = null;
    }

    private void RegisterContribution(
        IServiceCollection services,
        IReadOnlyCollection<string> reservedCommands,
        IReadOnlyCollection<string> extensionPoints,
        DiscoveredPlugin plugin,
        ICliPluginCapability capability,
        CliCommandContribution contribution,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(contribution.CommandPath) || string.IsNullOrWhiteSpace(contribution.CommandId))
        {
            errors.Add($"Plugin '{plugin.Plugin.Descriptor.Id}' has CLI contribution with missing command path or command id.");
            return;
        }

        var normalizedPath = NormalizePath(contribution.CommandPath);
        var registration = new RegisteredCliContribution(plugin, capability, contribution);

        if (contribution.ContributionType == CliContributionType.Extension)
        {
            if (!extensionPoints.Any(point => point.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"Plugin '{plugin.Plugin.Descriptor.Id}' contributes extension '{contribution.CommandPath}' but no such extension point exists.");
                return;
            }

            services.AddKeyedSingleton<RegisteredCliContribution>(ToExtensionServiceKey(normalizedPath), registration);
            return;
        }

        if (reservedCommands.Any(command => command.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(
                $"Plugin '{plugin.Plugin.Descriptor.Id}' cannot register command '{contribution.CommandPath}' because it is reserved by the host.");
            return;
        }

        if (!commandPaths.Add(normalizedPath))
        {
            var existingOwner = commandOwners.TryGetValue(normalizedPath, out var owner) ? owner : "unknown";
            errors.Add(
                $"CLI command collision on '{contribution.CommandPath}' between '{existingOwner}' and '{plugin.Plugin.Descriptor.Id}'.");
            return;
        }

        commandOwners[normalizedPath] = plugin.Plugin.Descriptor.Id;
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

    private void RegisterHostCommandMetadata(IServiceCollection services)
    {
        var builtInCommands = new[]
        {
            "open",
            "convert",
            "fileformats list",
            "plugins list"
        };

        var extensionPoints = new[]
        {
            "open",
            "convert"
        };

        foreach (var builtInCommand in builtInCommands)
        {
            services.AddKeyedSingleton<string>(CommandRegistryKey, NormalizePath(builtInCommand));
        }

        foreach (var extensionPoint in extensionPoints)
        {
            services.AddKeyedSingleton<string>(ExtensionPointRegistryKey, NormalizePath(extensionPoint));
        }
    }
}

internal sealed record RegisteredCliContribution(
    DiscoveredPlugin Plugin,
    ICliPluginCapability Capability,
    CliCommandContribution Contribution);
