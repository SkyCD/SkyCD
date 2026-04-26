using CommandDotNet;
using SkyCD.Cli.Execution;

namespace SkyCD.Cli.Console.FileFormats;

[Command("list")]
internal sealed class FileFormatsListCommand
{
    [DefaultCommand]
    public async Task<int> Execute()
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new InvalidOperationException("CLI command context is missing.");
        return (int)await context.Host.ExecuteListFormatsAsync(
            context.JsonOutput,
            context.FileFormatManager,
            context.PluginDirectories);
    }
}
