using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Cli.Command;

internal static class FileFormatsCommandExecutor
{
    internal static async Task<CliExitCodes> ExecuteListFormatsAsync(
        TextWriter stdout,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        IReadOnlyList<string> pluginDirectories)
    {
        var formats = fileFormatManager.GetOpenFormats()
            .Concat(fileFormatManager.GetSaveFormats())
            .GroupBy(static format => format.FormatId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static format => format.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(formats, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return CliExitCodes.Success;
        }

        if (formats.Count == 0)
        {
            await stdout.WriteLineAsync("No file format plugins were found.");
            await stdout.WriteLineAsync($"Plugin directories checked: {string.Join(", ", pluginDirectories)}");
            return CliExitCodes.Success;
        }

        foreach (var format in formats)
        {
            await stdout.WriteLineAsync($"{format.FormatId,-16} {format.DisplayName} [{string.Join(", ", format.Extensions)}]");
        }

        return CliExitCodes.Success;
    }
}
