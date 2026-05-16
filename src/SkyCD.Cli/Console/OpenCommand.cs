using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Enum;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Extensions;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Managers;

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
        return (int)await ExecuteOpenAsync(
            context.Host.Stdout,
            context.Host.Stderr,
            context.Host.JsonOptions,
            file,
            formatId,
            context.JsonOutput,
            context.FileFormatManager,
            context.CancellationToken);
    }

    private static async Task<CliExitCodes> ExecuteOpenAsync(
        TextWriter stdout,
        TextWriter stderr,
        JsonSerializerOptions jsonOptions,
        string? file,
        string? formatId,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            await stderr.WriteLineAsync("Missing required argument: <file>");
            return CliExitCodes.InvalidArguments;
        }

        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath))
        {
            await stderr.WriteLineAsync($"File not found: {fullPath}");
            return CliExitCodes.InvalidArguments;
        }

        var resolvedFormat = fileFormatManager.ResolveFormatId(formatId, fullPath, forWrite: false);
        await using var source = File.OpenRead(fullPath);
        await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedFormat,
            Source = source,
            FileName = Path.GetFileName(fullPath)
        }, cancellationToken);

        if (jsonOutput)
        {
            await stdout.WriteJsonAsync(new
            {
                success = true,
                command = "open",
                file = fullPath,
                formatId = resolvedFormat
            }, jsonOptions);
        }
        else
        {
            await stdout.WriteLineAsync($"Opened '{fullPath}' as {resolvedFormat}.");
        }

        return CliExitCodes.Success;
    }
}