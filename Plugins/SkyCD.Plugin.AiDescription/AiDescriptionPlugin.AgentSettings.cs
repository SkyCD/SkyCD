using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiDescription;

public sealed partial class AiDescriptionPlugin
{
    public AcpAgentSettings GetAgentSettings()
    {
        var options = ReadAgentSettings();
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        var registryPath = Path.Combine(directory, AcpAgentRegistry.FileName);
        List<AcpAgentChoice> agents = [];
        string? status = null;
        try
        {
            var searchDirectories = new[] { Path.Combine(directory, "acp-agents") }
                .Concat((System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
                .Where(Path.IsPathFullyQualified)
                .ToArray();
            foreach (var agent in AcpAgentRegistry.Load(registryPath).Agents)
            {
                var executable = FindExecutable(agent.BinaryCommand, searchDirectories);
                agents.Add(new AcpAgentChoice(agent.Name, executable, agent.BinaryArguments));
            }
        }
        catch (AcpRegistryException exception)
        {
            status = $"{exception.Message}. Save a registry copy from {AcpAgentRegistry.OfficialIndexUrl} to {registryPath} to browse it here.";
        }

        AcpAgentChoice? selected = null;
        if (!string.IsNullOrWhiteSpace(options.Executable))
        {
            selected = agents.FirstOrDefault(agent => SameExecutable(agent.Executable, options.Executable)
                && agent.Arguments.SequenceEqual(options.Arguments));
            if (selected is null)
            {
                var installed = Path.IsPathFullyQualified(options.Executable) && File.Exists(options.Executable);
                selected = new AcpAgentChoice(Path.GetFileName(options.Executable), installed ? options.Executable : null, options.Arguments);
                agents.Insert(0, selected);
            }
        }

        return new AcpAgentSettings(agents, selected, options.Enabled, status);
    }

    public void SaveAgentSettings(AcpAgentChoice? agent, bool enabled)
    {
        var options = ReadAgentSettings();
        if (agent?.Executable is { } executable)
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
                throw new DescriptionAgentException("select an installed ACP executable");
            if (!SameExecutable(executable, options.Executable) || !agent.Arguments.SequenceEqual(options.Arguments))
            {
                if (!IsExecutable(executable))
                    throw new DescriptionAgentException("the selected ACP file is not executable");
                options = options with
                {
                    Executable = executable,
                    Arguments = agent.Arguments,
                    Environment = new Dictionary<string, string>(),
                    AuthMethodId = null,
                    Model = null,
                    ProcessingDestination = "unknown"
                };
            }
        }
        else if (enabled)
        {
            throw new DescriptionAgentException("select an installed ACP executable");
        }

        options = options with { Enabled = enabled };
        if (enabled) options.Validate();
        var fullPath = Path.GetFullPath(configurationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private DescriptionAgentOptions ReadAgentSettings()
    {
        var info = new FileInfo(configurationPath);
        if (!info.Exists) return new DescriptionAgentOptions();
        if (info.Length > 65536) throw new DescriptionAgentException("configuration exceeds 64 KiB");
        return JsonSerializer.Deserialize<DescriptionAgentOptions>(File.ReadAllText(configurationPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
            ?? throw new DescriptionAgentException("configuration is invalid");
    }

    private static bool SameExecutable(string? first, string second) => string.Equals(first, second,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? FindExecutable(string? command, IEnumerable<string> searchDirectories)
    {
        if (command is null) return null;
        if (command.StartsWith("./", StringComparison.Ordinal) || command.StartsWith(".\\", StringComparison.Ordinal))
            command = command[2..];
        if (command.Length == 0 || command is "." or ".." || command.IndexOfAny(['/', '\\', ':']) >= 0 ||
            command.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        if (OperatingSystem.IsWindows())
        {
            if (!Path.HasExtension(command)) command += ".exe";
            if (!command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        }

        foreach (var directory in searchDirectories)
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate) && IsExecutable(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        try
        {
            return (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
