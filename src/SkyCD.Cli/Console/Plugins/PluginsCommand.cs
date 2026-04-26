using CommandDotNet;

namespace SkyCD.Cli.Console.Plugins;

[Command("plugins")]
internal sealed class PluginsCommand
{
    [Subcommand]
    public PluginsListSubcommand List { get; } = new();
}
