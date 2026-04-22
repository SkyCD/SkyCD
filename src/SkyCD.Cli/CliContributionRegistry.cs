using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Cli;

internal sealed class CliContributionRegistry
{
    private static readonly StringComparer CommandComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, RegisteredCliContribution> commands = new(CommandComparer);
    private readonly Dictionary<string, List<RegisteredCliContribution>> extensions = new(CommandComparer);
    private readonly HashSet<string> builtInCommands;
    private readonly HashSet<string> extensionPoints;

    public CliContributionRegistry(IEnumerable<string> builtInCommands, IEnumerable<string> extensionPoints)
    {
        this.builtInCommands = new HashSet<string>(builtInCommands.Select(NormalizePath), CommandComparer);
        this.extensionPoints = new HashSet<string>(extensionPoints.Select(NormalizePath), CommandComparer);
    }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public void Register(IEnumerable<DiscoveredPlugin> plugins)
    {
        var errors = new List<string>();

        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Capabilities.OfType<ICliPluginCapability>())
            {
                foreach (var contribution in capability.GetCliContributions())
                {
                    RegisterContribution(plugin, capability, contribution, errors);
                }
            }
        }

        foreach (var extension in extensions.Values)
        {
            extension.Sort(static (left, right) =>
            {
                var byPriority = right.Contribution.Priority.CompareTo(left.Contribution.Priority);
                if (byPriority != 0)
                {
                    return byPriority;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(left.Plugin.Plugin.Descriptor.Id, right.Plugin.Plugin.Descriptor.Id);
            });
        }

        Errors = errors;
    }

    public IReadOnlyCollection<string> CommandPaths => commands.Keys.ToArray();

    public RegisteredCliContribution? ResolveCommand(IReadOnlyList<string> args, out int consumedTokens)
    {
        consumedTokens = 0;
        for (var index = args.Count; index >= 1; index--)
        {
            var path = NormalizePath(args.Take(index));
            if (!commands.TryGetValue(path, out var contribution))
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
        return extensions.TryGetValue(NormalizePath(extensionPoint), out var contributions)
            ? contributions
            : [];
    }

    private void RegisterContribution(
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
            if (!extensionPoints.Contains(normalizedPath))
            {
                errors.Add(
                    $"Plugin '{plugin.Plugin.Descriptor.Id}' contributes extension '{contribution.CommandPath}' but no such extension point exists.");
                return;
            }

            if (!extensions.TryGetValue(normalizedPath, out var list))
            {
                list = [];
                extensions[normalizedPath] = list;
            }

            list.Add(registration);
            return;
        }

        if (builtInCommands.Contains(normalizedPath))
        {
            errors.Add(
                $"Plugin '{plugin.Plugin.Descriptor.Id}' cannot register command '{contribution.CommandPath}' because it is reserved by the host.");
            return;
        }

        if (commands.TryGetValue(normalizedPath, out var existing))
        {
            errors.Add(
                $"CLI command collision on '{contribution.CommandPath}' between '{existing.Plugin.Plugin.Descriptor.Id}' and '{plugin.Plugin.Descriptor.Id}'.");
            return;
        }

        commands[normalizedPath] = registration;
    }

    private static string NormalizePath(string path)
    {
        return NormalizePath(path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string NormalizePath(IEnumerable<string> tokens)
    {
        return string.Join(' ', tokens).Trim();
    }
}

internal sealed record RegisteredCliContribution(
    DiscoveredPlugin Plugin,
    ICliPluginCapability Capability,
    CliCommandContribution Contribution);
