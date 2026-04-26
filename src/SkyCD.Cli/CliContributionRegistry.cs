using System.Reflection;
using CommandDotNet;
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
                RegisterCapabilityCommands(plugin, capability, reservedCommands, errors);
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

    private void RegisterCapabilityCommands(
        DiscoveredPlugin plugin,
        ICliPluginCapability capability,
        IReadOnlySet<string> reservedCommands,
        ICollection<string> errors)
    {
        var rootType = capability.GetType();
        var rootCommandName = GetDeclaredCommandName(rootType);
        if (string.IsNullOrWhiteSpace(rootCommandName))
        {
            errors.Add($"Plugin '{plugin.Id}' CLI capability '{rootType.FullName}' is missing [Command(\"name\")] attribute.");
            return;
        }

        RegisterCommandNode(plugin, capability, rootType, rootCommandName, reservedCommands, errors);
    }

    private void RegisterCommandNode(
        DiscoveredPlugin plugin,
        object instance,
        Type commandType,
        string commandPath,
        IReadOnlySet<string> reservedCommands,
        ICollection<string> errors)
    {
        var normalizedPath = NormalizePath(commandPath);

        var executeMethod = ResolveExecuteMethod(commandType);
        if (executeMethod is not null)
        {
            if (reservedCommands.Contains(normalizedPath))
            {
                errors.Add(
                    $"Plugin '{plugin.Id}' cannot register command '{normalizedPath}' because it is reserved by the host.");
            }
            else if (!commandPaths.Add(normalizedPath))
            {
                var existingOwner = commandOwners.TryGetValue(normalizedPath, out var owner) ? owner : "unknown";
                errors.Add(
                    $"CLI command collision on '{normalizedPath}' between '{existingOwner}' and '{plugin.Id}'.");
            }
            else
            {
                commandOwners[normalizedPath] = plugin.Id;
                commandHandlers[normalizedPath] = new RegisteredCliContribution(plugin, normalizedPath, instance, executeMethod);
            }
        }

        foreach (var subcommandProperty in GetSubcommandProperties(commandType))
        {
            var subcommandType = subcommandProperty.PropertyType;
            var subcommandName = GetDeclaredCommandName(subcommandType);
            if (string.IsNullOrWhiteSpace(subcommandName))
            {
                errors.Add(
                    $"Plugin '{plugin.Id}' subcommand '{subcommandType.FullName}' is missing [Command(\"name\")] attribute.");
                continue;
            }

            var subcommandInstance = subcommandProperty.GetValue(instance) ?? Activator.CreateInstance(subcommandType);
            if (subcommandInstance is null)
            {
                errors.Add(
                    $"Plugin '{plugin.Id}' could not instantiate CLI subcommand '{subcommandType.FullName}'.");
                continue;
            }

            RegisterCommandNode(
                plugin,
                subcommandInstance,
                subcommandType,
                $"{normalizedPath} {subcommandName}",
                reservedCommands,
                errors);
        }
    }

    private static MethodInfo? ResolveExecuteMethod(Type commandType)
    {
        return commandType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method =>
                method.Name.Equals("Execute", StringComparison.Ordinal)
                && method.GetParameters().Length <= 1
                && (method.GetParameters().Length == 0
                    || method.GetParameters()[0].ParameterType == typeof(CancellationToken)));
    }

    private static IEnumerable<PropertyInfo> GetSubcommandProperties(Type commandType)
    {
        return commandType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static property => property.GetCustomAttribute<SubcommandAttribute>() is not null);
    }

    private static string GetDeclaredCommandName(Type commandType)
    {
        var commandAttributeData = commandType.CustomAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType == typeof(CommandAttribute));
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
    object CommandInstance,
    MethodInfo ExecuteMethod);
