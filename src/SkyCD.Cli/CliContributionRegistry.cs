using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli;

internal sealed class CliContributionRegistry : IDisposable
{
    private static readonly StringComparer CommandComparer = StringComparer.OrdinalIgnoreCase;
    private readonly HashSet<string> commandPaths = new(CommandComparer);
    private readonly Dictionary<string, string> commandOwners = new(CommandComparer);
    private readonly Dictionary<string, RegisteredCliContribution> commandHandlers = new(CommandComparer);

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyCollection<string> CommandPaths => commandPaths.ToArray();

    public void Register(IEnumerable<ICliPluginCapability> capabilities)
    {
        commandPaths.Clear();
        commandOwners.Clear();
        commandHandlers.Clear();

        var errors = new List<string>();
        foreach (var capability in capabilities)
        {
            var ownerId = capability.GetType().Assembly.GetName().Name ?? capability.GetType().FullName ?? "unknown";
            RegisterContribution(ownerId, capability, errors);
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
        string ownerId,
        ICliPluginCapability capability,
        ICollection<string> errors)
    {
        var commandPath = GetDeclaredCommandName(capability.GetType());
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            errors.Add($"Plugin '{ownerId}' CLI capability '{capability.GetType().FullName}' is missing [Command(\"name\")] attribute.");
            return;
        }

        var normalizedPath = NormalizePath(commandPath);
        if (!commandPaths.Add(normalizedPath))
        {
            var existingOwner = commandOwners.TryGetValue(normalizedPath, out var owner) ? owner : "unknown";
            errors.Add(
                $"CLI command collision on '{normalizedPath}' between '{existingOwner}' and '{ownerId}'.");
            return;
        }

        commandOwners[normalizedPath] = ownerId;
        commandHandlers[normalizedPath] = new RegisteredCliContribution(ownerId, normalizedPath, capability);
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
    string OwnerId,
    string CommandPath,
    ICliPluginCapability CommandInstance);
