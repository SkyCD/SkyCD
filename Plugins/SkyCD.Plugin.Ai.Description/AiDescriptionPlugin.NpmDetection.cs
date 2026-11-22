using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Ai.Description.Models;
using SkyCD.Plugin.Ai.Description.Sources;

namespace SkyCD.Plugin.Ai.Description;

public sealed partial class AiDescriptionPlugin
{
    private static AcpAgentChoice? FindInstalledNpmAgent(AcpRegistryAgent agent, string[] searchDirectories)
    {
        if (agent.NpmPackage is not { } package) return null;
        var versionAt = package.IndexOf('@', 1);
        if (versionAt >= 0) package = package[..versionAt];
        var segments = package.Split('/');
        if (segments.Length != (package.StartsWith('@') ? 2 : 1) || segments.Any(segment =>
            segment.TrimStart('@') is "" or "." or ".." ||
            segment.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or '@'))))
            return null;

        var node = FindExecutable("node", searchDirectories);
        if (node is null) return null;
        foreach (var directory in searchDirectories)
        {
            var modules = OperatingSystem.IsWindows()
                ? Path.Combine(directory, "node_modules")
                : Path.Combine(directory, "..", "lib", "node_modules");
            var root = Path.GetFullPath(Path.Combine(modules, package));
            var script = FindPackageScript(root, package, segments[^1]);
            if (script is not null && IsJavaScriptLauncher(script))
                return new AcpAgentChoice(agent.Name, node, [script, .. agent.NpmArguments]);
        }

        return null;
    }

    private static bool IsJavaScriptLauncher(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".js" or ".cjs" or ".mjs";
    }

    private static string? FindPackageScript(string root, string package, string command)
    {
        try
        {
            var manifest = new FileInfo(Path.Combine(root, "package.json"));
            if (!manifest.Exists || manifest.Length > 256 * 1024) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest.FullName));
            var metadata = document.RootElement;
            if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String || name.GetString() != package ||
                !metadata.TryGetProperty("bin", out var bin)) return null;

            if (bin.ValueKind == JsonValueKind.Object)
            {
                var commands = bin.EnumerateObject().ToArray();
                if (commands.Length == 1) bin = commands[0].Value;
                else if (bin.TryGetProperty(command, out var matchingCommand)) bin = matchingCommand;
                else return null;
            }

            if (bin.ValueKind != JsonValueKind.String || bin.GetString() is not { Length: > 0 } relative ||
                Path.IsPathRooted(relative)) return null;
            var script = Path.GetFullPath(relative, root);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return script.StartsWith(root + Path.DirectorySeparatorChar, comparison) && File.Exists(script) ? script : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
