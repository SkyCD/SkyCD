using CommandDotNet;
using SkyCD.SDK.Capabilities.Cli;

namespace SkyCD.Plugin.Feature.GenDesc.Cli;

/// <summary>Groups the <c>gendesc</c> commands: <c>gendesc nfo</c> and <c>gendesc description</c>.</summary>
[Command("gendesc",
    Description = "Generate .nfo files and torrent-site descriptions from GenDesc templates.")]
public sealed class GenDescCliCommand : ICliPluginCapability
{
    [Subcommand] public GenDescNfoCommand Nfo { get; } = new();

    [Subcommand] public GenDescDescriptionCommand Description { get; } = new();
}
