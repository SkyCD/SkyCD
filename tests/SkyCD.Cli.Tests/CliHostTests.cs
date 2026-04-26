using SkyCD.Cli;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text;

namespace SkyCD.Cli.Tests;

public sealed class CliHostTests
{
    [Fact]
    public async Task RootHelp_ShowsConciseCommandList()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Commands:", text, StringComparison.Ordinal);
        Assert.Contains("renamed-cli.exe [options] [command]", text, StringComparison.Ordinal);
        Assert.Contains("  open         Open and validate a catalog file.", text, StringComparison.Ordinal);
        Assert.Contains("  convert      Convert a catalog between supported formats.", text, StringComparison.Ordinal);
        Assert.Contains("  fileformats  Work with file format handlers.", text, StringComparison.Ordinal);
        Assert.Contains("  plugins      Inspect loaded plugins and capabilities.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("plugins list", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open <file>", text, StringComparison.Ordinal);
        Assert.Contains("renamed-cli.exe <command> --help", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RootHelp_WithWindowsSwitch_ShowsConciseCommandList()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["/?"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Commands:", text, StringComparison.Ordinal);
        Assert.Contains("renamed-cli.exe [options] [command]", text, StringComparison.Ordinal);
        Assert.Contains("  open         Open and validate a catalog file.", text, StringComparison.Ordinal);
        Assert.Contains("  plugins      Inspect loaded plugins and capabilities.", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void BuildPluginDirectories_UsesConfiguredAndAppSettingsPaths_AndDeduplicates()
    {
        var first = Path.Combine(Path.GetTempPath(), "skycd-cli-runtime-first");
        var second = Path.Combine(Path.GetTempPath(), "skycd-cli-runtime-second");
        var configured = string.Join(Path.PathSeparator, [first, second, first]);

        var directories = CliHost.BuildPluginDirectories(configured, second);

        Assert.Equal(2, directories.Count);
        Assert.Contains(Path.GetFullPath(first), directories, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(second), directories, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPluginDirectories_DoesNotAddLocalFallbackDirectories()
    {
        var directories = CliHost.BuildPluginDirectories(null, null);

        Assert.Empty(directories);
    }

    [Fact]
    public void TryReadPluginPathFromAppSettings_ReadsPluginPath()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "SkyCD"));

        try
        {
            var expectedPath = Path.Combine(appDataRoot, "CustomPlugins");
            var optionsPath = Path.Combine(appDataRoot, "SkyCD", "options.json");
            File.WriteAllText(optionsPath, JsonSerializer.Serialize(new { PluginPath = expectedPath }));

            var resolved = CliHost.TryReadPluginPathFromAppSettings(appDataRoot);

            Assert.Equal(expectedPath, resolved);
        }
        finally
        {
            Directory.Delete(appDataRoot, recursive: true);
        }
    }

    [Fact]
    public void TryReadPluginPathFromAppSettings_ReturnsNull_WhenFileMissingOrInvalid()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "SkyCD"));

        try
        {
            Assert.Null(CliHost.TryReadPluginPathFromAppSettings(appDataRoot));

            var optionsPath = Path.Combine(appDataRoot, "SkyCD", "options.json");
            File.WriteAllText(optionsPath, "{ not json");
            Assert.Null(CliHost.TryReadPluginPathFromAppSettings(appDataRoot));
        }
        finally
        {
            Directory.Delete(appDataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenHelp_ShowsCommandSpecificOptions()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["open", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.Contains("renamed-cli.exe open <file> [--format <id>] [--json]", text, StringComparison.Ordinal);
        Assert.Contains("--format <id>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("convert --in", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ConvertHelp_ShowsCommandSpecificOptions()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["convert", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe convert --in <file> --out <file>", text, StringComparison.Ordinal);
        Assert.Contains("--in-format <id>", text, StringComparison.Ordinal);
        Assert.Contains("--format <id>", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task PluginsHelp_ShowsListSubcommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["plugins", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe plugins <subcommand> [options]", text, StringComparison.Ordinal);
        Assert.Contains("list     List loaded plugins, capabilities, and commands", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task PluginsHelp_WithWindowsSwitch_ShowsListSubcommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["plugins", "/?"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe plugins <subcommand> [options]", text, StringComparison.Ordinal);
        Assert.Contains("list     List loaded plugins, capabilities, and commands", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FileFormatsHelp_ShowsListSubcommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["fileformats", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe fileformats <subcommand> [options]", text, StringComparison.Ordinal);
        Assert.Contains("list     List available read/write format handlers", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RootHelp_NormalizesDllNameToExe()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "SkyCD.App.dll");

        var result = await host.TryRunAsync(["--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("SkyCD.App.exe [options] [command]", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SkyCD.App.dll [options] [command]", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("SkyCD.App.dll [options] [command]", text, StringComparison.Ordinal);
        }

        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Plugins_WithoutSubcommand_ShowsPluginsHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["plugins"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe plugins <subcommand> [options]", text, StringComparison.Ordinal);
        Assert.Contains("list     List loaded plugins, capabilities, and commands", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FileFormats_WithoutSubcommand_ShowsFileFormatsHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["fileformats"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe fileformats <subcommand> [options]", text, StringComparison.Ordinal);
        Assert.Contains("list     List available read/write format handlers", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Plugins_WithoutSubcommand_WithJson_ShowsJsonHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["plugins", "--json"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("plugins", json.RootElement.GetProperty("command").GetString());
        Assert.Contains("renamed-cli.exe plugins <subcommand> [options]", json.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FileFormats_WithoutSubcommand_WithJson_ShowsJsonHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["fileformats", "--json"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("fileformats", json.RootElement.GetProperty("command").GetString());
        Assert.Contains("renamed-cli.exe fileformats <subcommand> [options]", json.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ListFormatsAlias_IsRejectedWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["list-formats"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'list-formats'. Did you mean 'fileformats list'?", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListFormatsAlias_Help_IsRejectedWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."),
            () => "renamed-cli.exe");

        var result = await host.TryRunAsync(["list-formats", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'list-formats'. Did you mean 'fileformats list'?", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginCommand_Executes_WithoutLaunchingUiPath()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["tests greet"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("hello from plugin", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task PluginCommand_IsCaseInsensitive()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["TeStS", "GrEeT"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("hello from plugin", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task PluginsList_AsConcatenatedToken_ReturnsInvalidArgumentsWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["pluginslist"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'pluginslist'. Did you mean 'plugins list'?", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileFormatsList_AsConcatenatedToken_ReturnsInvalidArgumentsWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["fileformatslist"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'fileformatslist'. Did you mean 'fileformats list'?", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_RunsExtensionPoint_AndTransformsPayload()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"skycd-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var inputPath = Path.Combine(temp, "input.src");
            var outputPath = Path.Combine(temp, "output.dst");
            await File.WriteAllTextAsync(inputPath, "seed", Encoding.UTF8);

            var output = new StringWriter();
            var error = new StringWriter();
            var host = CreateHost(output, error, CreateTestPlugins());

            var result = await host.TryRunAsync(["convert", "--in", inputPath, "--out", outputPath, "--in-format", "tests-read", "--format", "tests-write"]);

            Assert.True(result.Handled);
            Assert.Equal(CliExitCodes.Success, result.ExitCode);
            var converted = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.Equal("seed|ext", converted);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicatePluginCommand_ProducesCollisionError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins(includeDuplicateCommand: true));

        var result = await host.TryRunAsync(["tests greet"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.ConfigurationError, result.ExitCode);
        Assert.Contains("CLI command collision", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginCommand_CannotOverrideBuiltInCommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, [CreateSingleCapabilityPlugin("tests.cli.reserved", new ReservedCommandCapability())]);

        var result = await host.TryRunAsync(["open"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.ConfigurationError, result.ExitCode);
        Assert.Contains("cannot register command 'open' because it is reserved by the host", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginExtension_MustTargetKnownExtensionPoint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, [CreateSingleCapabilityPlugin("tests.cli.badext", new UnknownExtensionPointCapability())]);

        var result = await host.TryRunAsync(["convert"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.ConfigurationError, result.ExitCode);
        Assert.Contains("no such extension point exists", error.ToString(), StringComparison.Ordinal);
    }

    private static CliHost CreateHost(TextWriter stdout, TextWriter stderr, IEnumerable<DiscoveredPlugin> plugins)
    {
        var pluginList = plugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var serviceCollectionFactory = new ServiceCollectionFactory();
        var services = serviceCollectionFactory.BuildCommonServiceCollection();

        services.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            var pluginServices = serviceCollectionFactory.BuildPluginServiceCollection(plugin);
            foreach (var descriptor in pluginServices)
            {
                services.Add(descriptor);
            }
        }

        return new CliHost(
            stdout,
            stderr,
            (_, _) => Task.FromResult<IReadOnlyList<DiscoveredPlugin>>(pluginList));
    }

    private static IReadOnlyList<DiscoveredPlugin> CreateTestPlugins(bool includeDuplicateCommand = false)
    {
        var plugins = new List<DiscoveredPlugin>
        {
            new()
            {
                Id = "tests.cli",
                Name = "Tests CLI",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.cli.dll",
                Capabilities =
                [
                    new TestReadFormatCapability(),
                    new TestWriteFormatCapability(),
                    new TestGreetCliCapability(),
                    new TestConvertCliExtensionCapability()
                ]
            }
        };

        if (includeDuplicateCommand)
        {
            plugins.Add(new DiscoveredPlugin
            {
                Id = "tests.cli.dup",
                Name = "Tests CLI Dup",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.cli.dup.dll",
                Capabilities = [new DuplicateCommandCapability()]
            });
        }

        return plugins;
    }

    private static DiscoveredPlugin CreateSingleCapabilityPlugin(string id, IPluginCapability capability)
    {
        return new DiscoveredPlugin
        {
            Id = id,
            Name = id,
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = $"{id}.dll",
            Capabilities = [capability]
        };
    }

    private sealed class TestReadFormatCapability : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat => new("tests-read", "Tests Read", [".src"], CanRead: true, CanWrite: false);

        public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = await reader.ReadToEndAsync(cancellationToken)
            };
        }

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = false, Error = "read only" });
    }

    private sealed class TestWriteFormatCapability : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat => new("tests-write", "Tests Write", [".dst"], CanRead: false, CanWrite: true);

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = false, Error = "write only" });

        public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(request.Target, Encoding.UTF8, leaveOpen: true);
            await writer.WriteAsync(Convert.ToString(request.Payload));
            await writer.FlushAsync(cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
    }

    private sealed class TestGreetCliCapability : ICliPluginCapability
    {
        public CliCommandContribution Command => new("tests greet", "greet", "Greet command");

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true,
                Output = "hello from plugin"
            });
        }
    }

    private sealed class TestConvertCliExtensionCapability : ICliPluginCapability
    {
        public CliCommandContribution Command =>
            new("convert", "convert-ext", "Convert extension", CliContributionType.Extension, Priority: 10);

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true,
                Payload = $"{context.Payload}|ext"
            });
        }
    }

    private sealed class DuplicateCommandCapability : ICliPluginCapability
    {
        public CliCommandContribution Command => new("tests greet", "greet", "Conflicting command");

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true
            });
        }
    }

    private sealed class ReservedCommandCapability : ICliPluginCapability
    {
        public CliCommandContribution Command => new("open", "reserved-open", "Attempts to override built-in command");

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true
            });
        }
    }

    private sealed class UnknownExtensionPointCapability : ICliPluginCapability
    {
        public CliCommandContribution Command =>
            new("pack", "unknown-ext", "Unknown extension", CliContributionType.Extension);

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true
            });
        }
    }
}
