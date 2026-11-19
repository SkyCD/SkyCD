using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiDescription;

public sealed partial class AiDescriptionPlugin
{
    private static readonly HttpClient RegistryClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task RefreshRegistryAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var response = await RegistryClient.GetAsync(AcpAgentRegistry.OfficialIndexUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using (var file = File.Create(temporaryPath))
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    if (file.Length + count > AcpAgentRegistry.MaximumBytes)
                        throw new AcpRegistryException("download exceeds the 8 MiB limit");
                    await file.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                }
            }

            var registry = AcpAgentRegistry.Load(temporaryPath);
            if (registry.Agents.Count == 0)
                throw new AcpRegistryException("download contains no supported agents");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, Path.Combine(directory, AcpAgentRegistry.FileName), overwrite: true);
            await RefreshAgentIconsAsync(registry, directory, cancellationToken);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public AcpAgentSettings GetAgentSettings()
    {
        var options = ReadAgentSettings();
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        var registryPath = Path.Combine(directory, AcpAgentRegistry.FileName);
        List<AcpAgentChoice> agents = [];
        string? status = null;
        var localDirectory = Path.Combine(directory, "acp-agents");
        try
        {
            var searchDirectories = new[] { localDirectory }
                .Concat((System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
                .Where(Path.IsPathFullyQualified)
                .ToArray();
            foreach (var agent in AcpAgentRegistry.Load(registryPath).Agents)
            {
                var executable = FindExecutable(agent.BinaryCommand, searchDirectories);
                var npmChoice = executable is null ? FindInstalledNpmAgent(agent, searchDirectories) : null;
                var choice = ReadManagedAgent(agent, localDirectory) ?? npmChoice ??
                    new AcpAgentChoice(agent.Name, executable, agent.BinaryArguments);
                agents.Add(WithRegistryDetails(choice, agent) with { IconPaths = ReadAgentIcon(agent, directory) });
            }
        }
        catch (AcpRegistryException exception)
        {
            status = exception.Message;
        }

        if (Directory.Exists(localDirectory))
        {
            foreach (var executable in Directory.EnumerateFiles(localDirectory).Where(IsExecutable))
            {
                if (agents.Any(agent => SameExecutable(agent.Executable, executable))) continue;
                agents.Add(new AcpAgentChoice(Path.GetFileName(executable), executable, []));
            }
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

        return new AcpAgentSettings(agents, selected, status) { IsGenerationEnabled = options.Enabled };
    }

    public void SaveAgentSettings(AcpAgentChoice agent, bool activate = false)
    {
        var options = ReadAgentSettings();
        if (agent.Executable is { } executable)
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
        else
        {
            throw new DescriptionAgentException("select an installed ACP executable");
        }

        if (activate) options = options with { Enabled = true };
        options.ValidateSettings();
        var content = JsonSerializer.SerializeToUtf8Bytes(options, new JsonSerializerOptions { WriteIndented = true });
        if (content.Length > 65536) throw new DescriptionAgentException("configuration exceeds 64 KiB");
        var fullPath = Path.GetFullPath(configurationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporaryPath, fileOptions))
                stream.Write(content);
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
        var options = JsonSerializer.Deserialize<DescriptionAgentOptions>(File.ReadAllText(configurationPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
            ?? throw new DescriptionAgentException("configuration is invalid");
        return options with { Arguments = options.Arguments ?? [] };
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
