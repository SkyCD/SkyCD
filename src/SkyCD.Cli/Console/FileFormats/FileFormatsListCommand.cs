using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Command;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli.Console.FileFormats;

[Command("list")]
internal sealed class FileFormatsListCommand : ICliPluginCapability
{
    [DefaultCommand]
    public async Task<int> Execute()
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new CliCommandContextMissingException();
        return (int)await FileFormatsCommandExecutor.ExecuteListFormatsAsync(
            System.Console.Out,
            context.JsonOutput,
            context.FileFormatManager,
            context.PluginDirectories);
    }
}
