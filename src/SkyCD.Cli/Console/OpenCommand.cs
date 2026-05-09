using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Cli.Console;

[Command("open")]
internal sealed class OpenCommand : ICliPluginCapability
{
    [DefaultCommand]
    public async Task<int> Execute(
        [Operand] string? file = null,
        [Option("format")] string? formatId = null)
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new CliCommandContextMissingException();
        return (int)await context.Host.ExecuteOpenAsync(
            file,
            formatId,
            context.JsonOutput,
            context.FileFormatManager,
            context.CancellationToken);
    }
}
