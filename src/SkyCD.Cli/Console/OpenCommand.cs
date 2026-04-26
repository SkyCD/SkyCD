using CommandDotNet;
using SkyCD.Cli.Execution;

namespace SkyCD.Cli.Console;

[Command("open")]
internal sealed class OpenCommand
{
    [DefaultCommand]
    public async Task<int> Execute(
        [Operand] string? file = null,
        [Option("format")] string? formatId = null)
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new InvalidOperationException("CLI command context is missing.");
        return (int)await context.Host.ExecuteOpenAsync(
            file,
            formatId,
            context.JsonOutput,
            context.FileFormatManager,
            context.HostApi,
            context.Registry,
            context.CancellationToken);
    }
}
