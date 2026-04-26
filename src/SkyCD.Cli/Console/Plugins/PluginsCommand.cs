using CommandDotNet;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli.Console.Plugins;

[Command("plugins")]
internal sealed class PluginsCommand : ICliPluginCapability
{
    [Subcommand]
    public PluginsListSubcommand List { get; } = new();
}
