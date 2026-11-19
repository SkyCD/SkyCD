using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiDescription;

public sealed partial class AiDescriptionPlugin
{
    private const long MaxAgentBytes = 1024L * 1024 * 1024;
    private static readonly HttpClient AgentDownloads = new();
    private readonly HttpClient agentDownloadClient;
    private static readonly Regex NpmSpec = new(@"\A(?<name>(?:@[a-z0-9_.-]+/)?[a-z0-9][a-z0-9_.-]*)@[0-9][a-zA-Z0-9.+-]*\z");
    private static readonly Regex UvSpec = new(@"\A(?<name>[a-zA-Z0-9][a-zA-Z0-9_.-]*)(?:==|@)(?<version>[0-9][a-zA-Z0-9.+-]*)\z");

    private sealed record ManagedLaunch(string Directory, string EntryPoint, bool UsesNode, IReadOnlyList<string> Arguments)
    {
        public string? Version { get; init; }
        public string? RegistryId { get; init; }
        public string? Name { get; init; }
    }

    private static AcpAgentChoice WithRegistryDetails(AcpAgentChoice choice, AcpRegistryAgent agent) => choice with
    {
        RegistryId = agent.Id,
        RegistryVersion = agent.Version,
        Description = agent.Description,
        ExpectedExecutable = NativeFileName(agent.BinaryCommand),
        CanInstall = !choice.IsInstalled && !choice.IsManaged && HasInstaller(agent),
        CanUpdate = choice.IsManaged && HasInstaller(agent) && IsNewerVersion(agent.Version, choice.InstalledVersion)
    };

    private static bool HasInstaller(AcpRegistryAgent agent) => GetNativeUri(agent) is not null ||
        NpmSpec.IsMatch(agent.NpmPackage ?? string.Empty) || UvSpec.IsMatch(agent.UvPackage ?? string.Empty);

    private static bool IsNewerVersion(string incoming, string? installed)
    {
        if (string.Equals(incoming, installed, StringComparison.OrdinalIgnoreCase)) return false;
        return !Version.TryParse(incoming, out var newVersion) || !Version.TryParse(installed, out var oldVersion) || newVersion > oldVersion;
    }

    public Task<AcpAgentChoice> InstallAgentAsync(string registryId, CancellationToken cancellationToken)
        => InstallOrUpdateAgentAsync(registryId, update: false, cancellationToken);

    public Task<AcpAgentChoice> UpdateAgentAsync(string registryId, CancellationToken cancellationToken)
        => InstallOrUpdateAgentAsync(registryId, update: true, cancellationToken);

    private async Task<AcpAgentChoice> InstallOrUpdateAgentAsync(string registryId, bool update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = LockManagedSettings();
        return await InstallAgentCoreAsync(registryId, update, cancellationToken);
    }

    private FileStream LockManagedSettings()
    {
        var path = Path.GetFullPath(configurationPath) + ".agent-lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
    }

    private async Task<AcpAgentChoice> InstallAgentCoreAsync(string registryId, bool update, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        var agent = AcpAgentRegistry.Load(Path.Combine(directory, AcpAgentRegistry.FileName)).Find(registryId)
            ?? throw new DescriptionAgentException("the selected agent is no longer in the registry");
        var local = Path.Combine(directory, "acp-agents");
        var previous = ReadManagedLaunch(local, agent.Id);
        if (update && previous is null) throw new DescriptionAgentException("SkyCD did not install this agent");
        if (previous is not null && (!update || !IsNewerVersion(agent.Version, previous.Version)) &&
            ResolveManagedAgent(agent.Id, agent.Name, local, previous) is { IsInstalled: true } existing)
            return WithRegistryDetails(existing, agent);

        var name = Guid.NewGuid().ToString("N");
        var installedDirectory = Path.Combine(local, name);
        var receipt = ReceiptPath(local, agent.Id);
        var temporaryReceipt = receipt + "." + name + ".tmp";
        Directory.CreateDirectory(installedDirectory);
        var committed = false;
        try
        {
            var native = GetNativeUri(agent);
            var launch = native is not null
                ? await InstallNativeAgentAsync(agent, native, installedDirectory, name, cancellationToken)
                : NpmSpec.IsMatch(agent.NpmPackage ?? string.Empty)
                    ? await InstallNpmAgentAsync(agent, installedDirectory, name, cancellationToken)
                    : await InstallUvAgentAsync(agent, installedDirectory, name, cancellationToken);
            launch = launch with { Version = agent.Version, RegistryId = agent.Id, Name = agent.Name };
            var choice = ResolveManagedAgent(agent.Id, agent.Name, local, launch);
            if (!choice.IsInstalled) throw new DescriptionAgentException("the installed agent's launch file could not be found");
            await File.WriteAllTextAsync(temporaryReceipt, JsonSerializer.Serialize(launch), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var options = previous is not null ? ReadAgentSettings() : null;
            var migrate = previous is not null && options is not null && UsesManagedLaunch(options, local, previous);
            if (migrate)
            {
                var arguments = previous!.UsesNode ? options!.Arguments.Skip(1) : options!.Arguments;
                if (arguments.SequenceEqual(previous.Arguments)) arguments = launch.Arguments;
                if (launch.UsesNode) arguments = [choice.Arguments[0], .. arguments];
                WriteAgentSettings(options! with { Executable = choice.Executable!, Arguments = arguments.ToArray() });
            }
            try
            {
                File.Move(temporaryReceipt, receipt, overwrite: true);
            }
            catch
            {
                if (migrate) WriteAgentSettings(options!);
                throw;
            }
            committed = true;
            if (previous is not null) TryDeleteRetiredInstall(ManagedDirectory(local, previous));
            return WithRegistryDetails(choice, agent);
        }
        finally
        {
            File.Delete(temporaryReceipt);
            if (!committed) Directory.Delete(installedDirectory, recursive: true);
        }
    }

    private static ManagedLaunch? ReadManagedLaunch(string directory, string registryId)
    {
        try
        {
            var receipt = new FileInfo(ReceiptPath(directory, registryId));
            if (!receipt.Exists || receipt.Length > 64 * 1024) return null;
            var launch = JsonSerializer.Deserialize<ManagedLaunch>(File.ReadAllText(receipt.FullName));
            if (launch is null || launch.Arguments is null || string.IsNullOrWhiteSpace(launch.EntryPoint) ||
                (launch.RegistryId is not null && launch.RegistryId != registryId)) return null;
            _ = SafeChildPath(ManagedDirectory(directory, launch), launch.EntryPoint);
            return launch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or DescriptionAgentException)
        {
            return null;
        }
    }

    public void UninstallAgent(string registryId)
    {
        using var operation = LockManagedSettings();
        var local = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configurationPath))!, "acp-agents");
        var launch = ReadManagedLaunch(local, registryId)
            ?? throw new DescriptionAgentException("SkyCD did not install this agent");
        var installed = ManagedDirectory(local, launch);
        var retired = Path.Combine(local, ".retired-" + Guid.NewGuid().ToString("N"));
        var options = ReadAgentSettings();
        var selected = UsesManagedLaunch(options, local, launch);
        var moved = Directory.Exists(installed);
        if (moved) Directory.Move(installed, retired);
        var cleared = false;
        try
        {
            if (selected)
            {
                WriteAgentSettings(options with
                {
                    Enabled = false,
                    Executable = string.Empty,
                    Arguments = [],
                    Environment = new Dictionary<string, string>(),
                    AuthMethodId = null,
                    Model = null,
                    ProcessingDestination = "unknown"
                });
                cleared = true;
            }
            File.Delete(ReceiptPath(local, registryId));
        }
        catch
        {
            if (moved) Directory.Move(retired, installed);
            if (cleared) WriteAgentSettings(options);
            throw;
        }
        if (moved) TryDeleteRetiredInstall(retired);
    }

    private static string ManagedDirectory(string root, ManagedLaunch launch)
    {
        if (!Guid.TryParseExact(launch.Directory, "N", out _))
            throw new DescriptionAgentException("the install record points outside SkyCD's managed installations");
        var directory = Path.Combine(root, launch.Directory);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint) ||
            (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)))
            throw new DescriptionAgentException("the managed install folder redirects outside its expected location");
        return directory;
    }

    private static bool UsesManagedLaunch(DescriptionAgentOptions options, string root, ManagedLaunch launch)
    {
        var entryPoint = SafeChildPath(ManagedDirectory(root, launch), launch.EntryPoint);
        return launch.UsesNode
            ? options.Arguments.Count > 0 && SameExecutable(options.Arguments[0], entryPoint)
            : SameExecutable(options.Executable, entryPoint);
    }

    private static void TryDeleteRetiredInstall(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An existing agent process can still hold files from the retired installation.
        }
    }

    private static AcpAgentChoice? ReadUnlistedManagedAgent(string receipt, string directory)
    {
        try
        {
            if (new FileInfo(receipt).Length > 64 * 1024) return null;
            var launch = JsonSerializer.Deserialize<ManagedLaunch>(File.ReadAllText(receipt));
            if (launch is not { RegistryId: { } id, Name: { } name, Arguments: not null } ||
                string.IsNullOrWhiteSpace(launch.EntryPoint) ||
                !SameExecutable(receipt, ReceiptPath(directory, id))) return null;
            return ResolveManagedAgent(id, name, directory, launch);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or DescriptionAgentException)
        {
            return null;
        }
    }

    private static AcpAgentChoice ResolveManagedAgent(string registryId, string name, string directory, ManagedLaunch launch)
    {
        var entryPoint = SafeChildPath(ManagedDirectory(directory, launch), launch.EntryPoint);
        var executable = File.Exists(entryPoint)
            ? launch.UsesNode ? FindExecutable("node", SystemSearchDirectories()) : IsExecutable(entryPoint) ? entryPoint : null
            : null;
        return new AcpAgentChoice(name, executable, launch.UsesNode ? [entryPoint, .. launch.Arguments] : launch.Arguments)
        {
            IsManaged = true,
            RegistryId = registryId,
            InstalledVersion = launch.Version
        };
    }

    private static string ReceiptPath(string directory, string id) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");

    private static string[] SystemSearchDirectories() => (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(Path.IsPathFullyQualified).ToArray();

    private static string? NativeFileName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var name = command.Replace('\\', '/').Split('/')[^1];
        return name.Length == 0 || (OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ? null : name;
    }

    private static Uri? GetNativeUri(AcpRegistryAgent agent) => NativeFileName(agent.BinaryCommand) is not null &&
        Uri.TryCreate(agent.BinaryArchive, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    private async Task<ManagedLaunch> InstallNativeAgentAsync(AcpRegistryAgent agent, Uri uri, string directory,
        string name, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(directory, ".download");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using (var response = await agentDownloadClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var destination = File.Create(archive);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            int count;
            while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (destination.Length + count > MaxAgentBytes)
                    throw new DescriptionAgentException("the agent download exceeds 1 GiB");
                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }

            if (agent.BinaryChecksum is { } expected &&
                !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase))
                throw new DescriptionAgentException("the downloaded agent checksum did not match the registry");
        }

        var payload = Path.Combine(directory, "payload");
        Directory.CreateDirectory(payload);
        var expectedPath = SafeChildPath(payload, agent.BinaryCommand!);
        await Task.Run(() => ExtractAgent(archive, payload, expectedPath, timeout.Token), timeout.Token);
        File.Delete(archive);
        if (!File.Exists(expectedPath))
            throw new DescriptionAgentException($"the archive did not contain {agent.BinaryCommand}");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(expectedPath, File.GetUnixFileMode(expectedPath) | UnixFileMode.UserExecute);
        return new ManagedLaunch(name, Path.GetRelativePath(directory, expectedPath), false, agent.BinaryArguments);
    }

    private static void ExtractAgent(string archive, string destination, string executable, CancellationToken cancellationToken)
    {
        using var source = File.OpenRead(archive);
        Span<byte> signature = stackalloc byte[4];
        var bytes = source.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        source.Position = 0;
        long total = 0;
        var entries = 0;
        void CheckEntry(long length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > 15000 || length > MaxAgentBytes - total)
                throw new DescriptionAgentException("the extracted agent archive exceeds its size limit");
            total += length;
        }

        if (bytes == 4 && signature[0] == 'P' && signature[1] == 'K')
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                CheckEntry(entry.Length);
                if (entry.FullName is "./" or ".\\") continue;
                var path = SafeChildPath(destination, entry.FullName);
                if (entry.Name.Length == 0) Directory.CreateDirectory(path);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = entry.Open();
                    using var output = File.Open(path, FileMode.CreateNew);
                    var buffer = new byte[65536];
                    long written = 0;
                    int count;
                    while ((count = input.Read(buffer)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (written + count > entry.Length)
                            throw new DescriptionAgentException("the archive entry exceeds its advertised size");
                        output.Write(buffer, 0, count);
                        written += count;
                    }
                    SetArchiveMode(path, (entry.ExternalAttributes >> 16) & 511);
                }
            }
        }
        else if (bytes >= 2 && signature[0] == 31 && signature[1] == 139)
        {
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                CheckEntry(entry.Length);
                if (entry.EntryType == TarEntryType.Directory) continue;
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new DescriptionAgentException("the archive contains an unsupported entry type");
                var path = SafeChildPath(destination, entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = File.Open(path, FileMode.CreateNew);
                entry.DataStream?.CopyTo(output);
                SetArchiveMode(path, (int)entry.Mode);
            }
        }
        else
        {
            // The registry also supports direct Windows, ELF and Mach-O binaries.
            var magic = bytes == 4 ? Convert.ToHexString(signature) : string.Empty;
            if (!magic.StartsWith("4D5A", StringComparison.Ordinal) && magic is not
                ("7F454C46" or "FEEDFACE" or "CEFAEDFE" or "FEEDFACF" or "CFFAEDFE" or "CAFEBABE" or "CAFEBABF"))
                throw new DescriptionAgentException("the agent archive format is unsupported");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            using var output = File.Open(executable, FileMode.CreateNew);
            source.CopyTo(output);
        }
    }

    private static void SetArchiveMode(string path, int mode)
    {
        if (!OperatingSystem.IsWindows() && (mode & 73) != 0)
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
    }

    private static string SafeChildPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new DescriptionAgentException("the agent archive contains an absolute path");
        var path = Path.GetFullPath(relative.Replace('\\', '/'), root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new DescriptionAgentException("the agent archive contains a path outside its install folder");
        return path;
    }

    private static async Task<ManagedLaunch> InstallNpmAgentAsync(AcpRegistryAgent agent, string directory, string name,
        CancellationToken cancellationToken)
    {
        var package = agent.NpmPackage ?? string.Empty;
        var match = NpmSpec.Match(package);
        if (!match.Success) throw new DescriptionAgentException("there is no supported installer for this agent");
        var search = SystemSearchDirectories();
        var node = FindExecutable("node", search) ?? throw new DescriptionAgentException("install Node.js with npm first");
        var npm = OperatingSystem.IsWindows() ? node : FindExecutable("npm", search);
        if (npm is null) throw new DescriptionAgentException("install Node.js with npm first");
        var start = new ProcessStartInfo(npm) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        if (OperatingSystem.IsWindows())
        {
            var npmScript = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
            if (!File.Exists(npmScript)) throw new DescriptionAgentException("install Node.js with npm first");
            start.ArgumentList.Add(npmScript);
        }
        foreach (var argument in new[] { "install", "--prefix", directory, "--no-audit", "--no-fund", "--", package })
            start.ArgumentList.Add(argument);
        await RunInstallerAsync(start, "npm", cancellationToken);
        var packageName = match.Groups["name"].Value;
        var root = Path.GetFullPath(Path.Combine(directory, "node_modules", packageName));
        var script = FindPackageScript(root, packageName, packageName.Split('/')[^1]);
        if (script is null || !IsJavaScriptLauncher(script))
            throw new DescriptionAgentException("the npm package did not provide a JavaScript ACP launch script");
        return new ManagedLaunch(name, Path.GetRelativePath(directory, script), true, agent.NpmArguments);
    }

    private static async Task<ManagedLaunch> InstallUvAgentAsync(AcpRegistryAgent agent, string directory, string name,
        CancellationToken cancellationToken)
    {
        var match = UvSpec.Match(agent.UvPackage ?? string.Empty);
        if (!match.Success) throw new DescriptionAgentException("there is no supported installer for this agent");
        var uv = FindExecutable("uv", SystemSearchDirectories()) ?? throw new DescriptionAgentException("install uv first");
        var package = match.Groups["name"].Value;
        var scripts = Path.Combine(directory, "bin");
        var start = new ProcessStartInfo(uv) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        start.Environment["UV_TOOL_DIR"] = Path.Combine(directory, "tools");
        start.Environment["UV_TOOL_BIN_DIR"] = scripts;
        foreach (var argument in new[] { "tool", "install", "--no-config", "--", package + "==" + match.Groups["version"].Value })
            start.ArgumentList.Add(argument);
        await RunInstallerAsync(start, "uv", cancellationToken);
        var executable = FindExecutable(package, [scripts]);
        if (executable is null && Directory.Exists(scripts))
        {
            var candidates = Directory.EnumerateFiles(scripts).Where(IsExecutable).Take(2).ToArray();
            if (candidates.Length == 1) executable = candidates[0];
        }
        if (executable is null) throw new DescriptionAgentException("the uv package did not provide an unambiguous ACP launch file");
        return new ManagedLaunch(name, Path.GetRelativePath(directory, executable), false, agent.UvArguments);
    }

    private static async Task RunInstallerAsync(ProcessStartInfo start, string installer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new DescriptionAgentException($"{installer} could not be started");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        if (process.ExitCode != 0) throw new DescriptionAgentException($"{installer} exited with code {process.ExitCode}");
    }
}
