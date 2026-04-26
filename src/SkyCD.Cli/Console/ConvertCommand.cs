using CommandDotNet;
using SkyCD.Cli.Execution;

namespace SkyCD.Cli.Console;

[Command("convert")]
internal sealed class ConvertCommand
{
    [DefaultCommand]
    public async Task<int> Execute(
        [Option("in")] string? inputPath = null,
        [Option("out")] string? outputPath = null,
        [Option("in-format")] string? inputFormat = null,
        [Option("format")] string? outputFormat = null)
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new InvalidOperationException("CLI command context is missing.");
        return (int)await context.Host.ExecuteConvertAsync(
            inputPath,
            outputPath,
            inputFormat,
            outputFormat,
            context.JsonOutput,
            context.FileFormatManager,
            context.HostApi,
            context.Registry,
            context.CancellationToken);
    }
}
