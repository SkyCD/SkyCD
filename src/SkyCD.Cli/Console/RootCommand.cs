using CommandDotNet;
using SkyCD.Cli.Console.FileFormats;
using SkyCD.Cli.Console.Plugins;

namespace SkyCD.Cli.Console;

[Command("skycd")]
internal sealed class RootCommand
{
    [Subcommand]
    public OpenCommand Open { get; } = new();

    [Subcommand]
    public ConvertCommand Convert { get; } = new();

    [Subcommand]
    public FileFormatsCommand FileFormats { get; } = new();

    [Subcommand]
    public PluginsCommand Plugins { get; } = new();
}
