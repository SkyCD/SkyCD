using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Enum;
using SkyCD.Cli.Execution;
using SkyCD.Cli.Extensions;
using SkyCD.Cli.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
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
        return (int)await ExecuteListFormatsAsync(
            System.Console.Out,
            context.JsonOutput,
            context.Host.JsonOptions,
            context.FileFormatManager,
            context.PluginDirectory);
    }

    private static async Task<CliExitCodes> ExecuteListFormatsAsync(
        TextWriter stdout,
        bool jsonOutput,
        JsonSerializerOptions jsonOptions,
        FileFormatManager fileFormatManager,
        string? pluginDirectory)
    {
        var formats = fileFormatManager.GetOpenFormats()
            .Concat(fileFormatManager.GetSaveFormats())
            .GroupBy(static format => format.FormatId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static format => format.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteJsonAsync(formats, jsonOptions);
            return CliExitCodes.Success;
        }

        if (formats.Count == 0)
        {
            await stdout.WriteLineAsync("No file format plugins were found.");
            await stdout.WriteLineAsync($"Plugin directory checked: {pluginDirectory ?? "(not configured)"}");
            return CliExitCodes.Success;
        }

        foreach (var format in formats)
        {
            await stdout.WriteLineAsync(
                $"{format.FormatId,-16} {format.DisplayName} [{string.Join(", ", format.Extensions)}]");
        }

        return CliExitCodes.Success;
    }
}
