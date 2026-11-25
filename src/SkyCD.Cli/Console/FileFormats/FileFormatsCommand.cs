using CommandDotNet;
using SkyCD.SDK.Capabilities.Cli;

namespace SkyCD.Cli.Console.FileFormats;

[Command("fileformats")]
internal sealed class FileFormatsCommand : ICliPluginCapability
{
    [Subcommand] public FileFormatsListCommand List { get; } = new();
}