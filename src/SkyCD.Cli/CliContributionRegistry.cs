using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Cli;

internal sealed class CliContributionRegistry : IDisposable
{
    private static readonly StringComparer CommandComparer = StringComparer.OrdinalIgnoreCase;
    private readonly HashSet<string> commandPaths = new(CommandComparer);
    private readonly Dictionary<string, string> commandOwners = new(CommandComparer);
    private readonly Dictionary<string, RegisteredCliContribution> commandHandlers = new(CommandComparer);

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyCollection<string> CommandPaths => commandPaths.ToArray();

    public void Register(IEnumerable<DiscoveredPlugin> plugins)
    {
        commandPaths.Clear();
        commandOwners.Clear();
        commandHandlers.Clear();

        var errors = new List<string>();
        var reservedCommands = CliHost.GetSystemCommandPaths();

        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Capabilities.OfType<ICliPluginCapability>())
            {
                RegisterContribution(plugin, capability, reservedCommands, errors);
            }
        }

        Errors = errors;
    }

    public RegisteredCliContribution? ResolveCommand(IReadOnlyList<string> args, out int consumedTokens)
    {
        consumedTokens = 0;

        for (var index = args.Count; index >= 1; index--)
        {
            var path = NormalizePath(args.Take(index));
            if (!commandHandlers.TryGetValue(path, out var contribution))
            {
                continue;
            }

            consumedTokens = index;
            return contribution;
        }

        return null;
    }

    public void Dispose()
    {
        // Kept for compatibility with existing call sites and lifecycle patterns.
    }

    private void RegisterContribution(
        DiscoveredPlugin plugin,
        ICliPluginCapability capability,
        IReadOnlySet<string> reservedCommands,
        ICollection<string> errors)
    {
        var commandPath = GetDeclaredCommandName(capability.GetType());
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            errors.Add($"Plugin '{plugin.Id}' CLI capability '{capability.GetType().FullName}' is missing [Command(\"name\")] attribute.");
            return;
        }

        var normalizedPath = NormalizePath(commandPath);
        if (reservedCommands.Contains(normalizedPath))
        {
            errors.Add(
                $"Plugin '{plugin.Id}' cannot register command '{normalizedPath}' because it is reserved by the host.");
            return;
        }

        if (!commandPaths.Add(normalizedPath))
        {
            var existingOwner = commandOwners.TryGetValue(normalizedPath, out var owner) ? owner : "unknown";
            errors.Add(
                $"CLI command collision on '{normalizedPath}' between '{existingOwner}' and '{plugin.Id}'.");
            return;
        }

        commandOwners[normalizedPath] = plugin.Id;
        commandHandlers[normalizedPath] = new RegisteredCliContribution(plugin, normalizedPath, capability);
    }

    private static string GetDeclaredCommandName(Type commandType)
    {
        var commandAttributeData = commandType.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.Name == "CommandAttribute");
        if (commandAttributeData is null)
        {
            return string.Empty;
        }

        if (commandAttributeData.ConstructorArguments.Count > 0
            && commandAttributeData.ConstructorArguments[0].ArgumentType == typeof(string)
            && commandAttributeData.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrWhiteSpace(ctorValue))
        {
            return ctorValue.Trim();
        }

        return string.Empty;
    }

    private static string NormalizePath(IEnumerable<string> tokens)
    {
        return string.Join(' ', tokens)
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        return NormalizePath(path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

internal sealed record RegisteredCliContribution(
    DiscoveredPlugin Plugin,
    string CommandPath,
    ICliPluginCapability CommandInstance);
