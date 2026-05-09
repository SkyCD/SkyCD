using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Command;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli.Console.Plugins;

[Command("list")]
internal sealed class PluginsListSubcommand : ICliPluginCapability
{
    [DefaultCommand]
    public async Task<int> Execute()
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new CliCommandContextMissingException();
        return (int)await PluginsCommandExecutor.ExecutePluginsListAsync(
            System.Console.Out,
            context.JsonOutput,
            context.Registry,
            context.FileFormatManager,
            context.DiscoveredPlugins,
            context.PluginDirectories);
    }
}
