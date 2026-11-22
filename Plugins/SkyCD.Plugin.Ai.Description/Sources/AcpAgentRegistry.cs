using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkyCD.Plugin.Ai.Description.Exceptions;

namespace SkyCD.Plugin.Ai.Description.Sources;

/// <summary>One cached registry entry. Registry content is descriptive data and is never executed.</summary>
public sealed record AcpRegistryAgent(string Id, string Name, string Version, string Description,
    IReadOnlyList<string> Distributions, string? LaunchHint)
{
    internal string? BinaryCommand { get; init; }
    internal IReadOnlyList<string> BinaryArguments { get; init; } = [];
    internal string? NpmPackage { get; init; }
    internal IReadOnlyList<string> NpmArguments { get; init; } = [];
    internal string? BinaryArchive { get; init; }
    internal string? BinaryChecksum { get; init; }
    internal string? IconUrl { get; init; }
    internal string? UvPackage { get; init; }
    internal IReadOnlyList<string> UvArguments { get; init; } = [];
}

/// <summary>Reads a locally cached copy of the official ACP registry index for offline browsing.</summary>
public sealed class AcpAgentRegistry
{
    public const string OfficialIndexUrl = "https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json";
    public const string FileName = "acp-registry.json";
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const int MaximumAgents = 512;

    private AcpAgentRegistry(IReadOnlyList<AcpRegistryAgent> agents) => Agents = agents;

    public IReadOnlyList<AcpRegistryAgent> Agents { get; }

    public AcpRegistryAgent? Find(string id) => Agents.FirstOrDefault(agent => agent.Id == id);

    public static AcpAgentRegistry Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new AcpRegistryException("the cached registry file was not found");
        if (info.Length > MaximumBytes) throw new AcpRegistryException("the cached registry file exceeds the 8 MiB limit");
        byte[] content;
        try
        {
            content = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            throw new AcpRegistryException("the cached registry file could not be read");
        }
        catch (UnauthorizedAccessException)
        {
            throw new AcpRegistryException("the cached registry file could not be read");
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            throw new AcpRegistryException("the cached registry file is not valid JSON");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String
                || version.GetString() is not { } versionText || !versionText.StartsWith("1.", StringComparison.Ordinal))
                throw new AcpRegistryException("the cached registry file has an unsupported format version");
            if (!root.TryGetProperty("agents", out var agents) || agents.ValueKind != JsonValueKind.Array)
                throw new AcpRegistryException("the cached registry file has no agent list");
            var parsed = new List<AcpRegistryAgent>();
            foreach (var entry in agents.EnumerateArray())
            {
                if (parsed.Count >= MaximumAgents) break;
                if (entry.ValueKind == JsonValueKind.Object && TryParse(entry, out var agent)) parsed.Add(agent);
            }
            return new AcpAgentRegistry(parsed);
        }
    }

    private static bool TryParse(JsonElement entry, out AcpRegistryAgent agent)
    {
        agent = null!;
        var id = BoundedString(entry, "id", 128);
        var name = BoundedString(entry, "name", 256);
        var version = BoundedString(entry, "version", 64);
        if (id is null || name is null || version is null) return false;
        if (!entry.TryGetProperty("distribution", out var distribution) || distribution.ValueKind != JsonValueKind.Object)
            return false;
        var description = BoundedString(entry, "description", 512) ?? string.Empty;
        var kinds = new List<string>();
        string? hint = null;
        string? binaryCommand = null;
        IReadOnlyList<string> binaryArguments = [];
        string? npmPackage = null;
        string? binaryArchive = null;
        string? binaryChecksum = null;
        string? uvPackage = null;
        IReadOnlyList<string> uvArguments = [];
        IReadOnlyList<string> npmArguments = [];
        if (distribution.TryGetProperty("npx", out var npx) && npx.ValueKind == JsonValueKind.Object)
        {
            kinds.Add("npx");
            npmPackage = BoundedString(npx, "package", 256);
            var launchArguments = LaunchArguments(npx);
            npmArguments = launchArguments ?? [];
            if (launchArguments is null) npmPackage = null;
            hint ??= PackageHint("npx", npx);
        }
        if (distribution.TryGetProperty("uvx", out var uvx) && uvx.ValueKind == JsonValueKind.Object)
        {
            kinds.Add("uvx");
            uvPackage = BoundedString(uvx, "package", 256);
            var launchArguments = LaunchArguments(uvx);
            uvArguments = launchArguments ?? [];
            if (launchArguments is null) uvPackage = null;
            hint ??= PackageHint("uvx", uvx);
        }
        if (distribution.TryGetProperty("binary", out var binary) && binary.ValueKind == JsonValueKind.Object)
        {
            kinds.Add("binary");
            var platform = PlatformKey();
            if (platform is not null && binary.TryGetProperty(platform, out var target) && target.ValueKind == JsonValueKind.Object)
            {
                binaryCommand = BoundedString(target, "cmd", 512);
                binaryArchive = BoundedString(target, "archive", 2048);
                binaryChecksum = BoundedString(target, "sha256", 64);
                var launchArguments = LaunchArguments(target);
                binaryArguments = launchArguments ?? [];
                if (launchArguments is null) binaryCommand = null;
                if (binaryCommand is not null)
                    hint ??= Bound($"binary ({platform}): install and run {string.Join(' ', new[] { binaryCommand }.Concat(binaryArguments).Select(Quote))}", 400);
            }
        }
        if (kinds.Count == 0) return false;
        agent = new AcpRegistryAgent(id, name, version, description, kinds, hint)
        {
            BinaryCommand = binaryCommand,
            BinaryArguments = binaryArguments,
            NpmPackage = npmPackage,
            NpmArguments = npmArguments,
            BinaryArchive = binaryArchive,
            BinaryChecksum = binaryChecksum,
            IconUrl = BoundedString(entry, "icon", 512),
            UvPackage = uvPackage,
            UvArguments = uvArguments
        };
        return true;
    }

    private static string? PackageHint(string launcher, JsonElement package)
    {
        var name = BoundedString(package, "package", 256);
        if (name is null) return null;
        var arguments = BoundedArguments(package);
        return Bound(string.Join(' ', new[] { launcher, name }.Concat(arguments).Select(Quote)), 400);
    }

    private static string? PlatformKey()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            Architecture.X64 => "x86_64",
            _ => null
        };
        if (architecture is null) return null;
        if (OperatingSystem.IsWindows()) return "windows-" + architecture;
        if (OperatingSystem.IsMacOS()) return "darwin-" + architecture;
        if (OperatingSystem.IsLinux()) return "linux-" + architecture;
        return null;
    }

    private static IReadOnlyList<string>? LaunchArguments(JsonElement owner)
    {
        if (!owner.TryGetProperty("args", out var args)) return [];
        if (args.ValueKind != JsonValueKind.Array) return null;
        var values = new List<string>();
        foreach (var argument in args.EnumerateArray())
        {
            if (argument.ValueKind != JsonValueKind.String) return null;
            values.Add(argument.GetString()!);
        }
        return values;
    }

    private static IReadOnlyList<string> BoundedArguments(JsonElement owner)
    {
        if (!owner.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array) return [];
        var values = new List<string>();
        foreach (var argument in args.EnumerateArray())
        {
            if (values.Count >= 32) break;
            if (argument.ValueKind == JsonValueKind.String && argument.GetString() is { Length: <= 512 } value)
                values.Add(value);
        }
        return values;
    }

    private static string? BoundedString(JsonElement owner, string property, int limit)
    {
        if (!owner.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) || text.Length > limit ? null : text;
    }

    private static string Bound(string value, int limit) => value.Length <= limit ? value : value[..limit];

    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "'") + "\"" : value;
}
