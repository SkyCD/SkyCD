using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Extensions;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Cli.Console;

[Command("convert")]
internal sealed class ConvertCommand : ICliPluginCapability
{
    [DefaultCommand]
    public async Task<int> Execute(
        [Option("in")] string? inputPath = null,
        [Option("out")] string? outputPath = null,
        [Option("in-format")] string? inputFormat = null,
        [Option("format")] string? outputFormat = null)
    {
        var context = CliCommandExecutionContextScope.Current
                      ?? throw new CliCommandContextMissingException();
        return (int)await ExecuteConvertAsync(
            context.Host.Stdout,
            context.Host.Stderr,
            context.Host.JsonOptions,
            inputPath,
            outputPath,
            inputFormat,
            outputFormat,
            context.JsonOutput,
            context.FileFormatManager,
            context.CancellationToken);
    }

    private static async Task<CliExitCodes> ExecuteConvertAsync(
        TextWriter stdout,
        TextWriter stderr,
        JsonSerializerOptions jsonOptions,
        string? inputPath,
        string? outputPath,
        string? inputFormat,
        string? outputFormat,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            await stderr.WriteLineAsync("Missing required options: --in <file> --out <file>");
            return CliExitCodes.InvalidArguments;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(fullInputPath))
        {
            await stderr.WriteLineAsync($"Input file not found: {fullInputPath}");
            return CliExitCodes.InvalidArguments;
        }

        var resolvedInputFormat = fileFormatManager.ResolveFormatId(inputFormat, fullInputPath, forWrite: false);
        var resolvedOutputFormat = fileFormatManager.ResolveFormatId(outputFormat, fullOutputPath, forWrite: true);

        await using var source = File.OpenRead(fullInputPath);
        var readResult = await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedInputFormat,
            Source = source,
            FileName = Path.GetFileName(fullInputPath)
        }, cancellationToken);

        var payload = readResult.Payload
                      ?? throw new CliSourcePayloadMissingException();
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory());

        await using var target = File.Create(fullOutputPath);
        await fileFormatManager.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = resolvedOutputFormat,
            Target = target,
            FileName = Path.GetFileName(fullOutputPath),
            Payload = payload
        }, cancellationToken);

        if (jsonOutput)
        {
            await stdout.WriteJsonAsync(new
            {
                success = true,
                command = "convert",
                inputPath = fullInputPath,
                outputPath = fullOutputPath,
                inputFormatId = resolvedInputFormat,
                outputFormatId = resolvedOutputFormat
            }, jsonOptions);
        }
        else
        {
            await stdout.WriteLineAsync(
                $"Converted '{fullInputPath}' ({resolvedInputFormat}) -> '{fullOutputPath}' ({resolvedOutputFormat}).");
        }

        return CliExitCodes.Success;
    }
}