using CommandDotNet;

namespace SkyCD.Cli.Console.FileFormats;

[Command("fileformats")]
internal sealed class FileFormatsCommand
{
    [Subcommand]
    public FileFormatsListCommand List { get; } = new();
}
