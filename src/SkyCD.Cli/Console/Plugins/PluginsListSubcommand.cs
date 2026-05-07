using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Execution;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli.Console.Plugins;

[Command("list")]
internal sealed class PluginsListSubcommand : ICliPluginCapability
{
    [DefaultCommand]
    public async Task<int> Execute()
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new InvalidOperationException("CLI command context is missing.");
        return (int)await context.Host.ExecutePluginsListAsync(
            context.JsonOutput,
            context.Registry,
            context.FileFormatManager,
            context.DiscoveredPlugins,
            context.PluginDirectories);
    }
}
