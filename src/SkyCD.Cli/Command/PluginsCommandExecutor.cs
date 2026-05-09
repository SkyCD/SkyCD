using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Cli.Execution;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Cli.Command;

internal static class PluginsCommandExecutor
{
    internal static async Task<CliExitCodes> ExecutePluginsListAsync(
        TextWriter stdout,
        bool jsonOutput,
        CliContributionRegistry registry,
        FileFormatManager fileFormatManager,
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories)
    {
        var availableFormatIds = fileFormatManager.GetOpenFormats()
            .Concat(fileFormatManager.GetSaveFormats())
            .Select(static format => format.FormatId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var formatsByPlugin = discoveredPlugins
            .ToDictionary(
                static plugin => plugin.Id,
                plugin => plugin.Capabilities
                    .OfType<IFileFormatPluginCapability>()
                    .Select(static capability => capability.SupportedFormat.FormatId)
                    .Where(formatId => availableFormatIds.Contains(formatId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static id => id)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase)
            .Where(static item => item.Value.Length > 0)
            .ToDictionary(
                static item => item.Key,
                static item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        var pluginInfo = discoveredPlugins
            .Select(plugin => new
            {
                PluginId = plugin.Id,
                DisplayName = plugin.Name,
                Capabilities = plugin.Capabilities.Select(static capability => capability.GetType().Name).OrderBy(static name => name).ToArray(),
                Formats = formatsByPlugin.TryGetValue(plugin.Id, out var formats)
                    ? formats
                    : Array.Empty<string>()
            })
            .OrderBy(static plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                plugins = pluginInfo,
                cliCommands = registry.CommandPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                pluginDirectories = pluginDirectories
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return CliExitCodes.Success;
        }

        if (pluginInfo.Count == 0)
        {
            await stdout.WriteLineAsync("No plugins loaded.");
        }
        else
        {
            foreach (var plugin in pluginInfo)
            {
                var formats = plugin.Formats.Length == 0 ? "-" : string.Join(", ", plugin.Formats);
                await stdout.WriteLineAsync($"{plugin.PluginId} ({plugin.DisplayName})");
                await stdout.WriteLineAsync($"  capabilities: {string.Join(", ", plugin.Capabilities)}");
                await stdout.WriteLineAsync($"  formats: {formats}");
            }
        }

        var pluginCommands = registry.CommandPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (pluginCommands.Length > 0)
        {
            await stdout.WriteLineAsync("Plugin CLI commands:");
            foreach (var path in pluginCommands)
            {
                await stdout.WriteLineAsync($"  {path}");
            }
        }

        await stdout.WriteLineAsync($"Plugin directories checked: {string.Join(", ", pluginDirectories)}");

        return CliExitCodes.Success;
    }
}
