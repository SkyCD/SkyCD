using SkyCD.Cli;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using Couchbase.Lite;
using Microsoft.Extensions.DependencyInjection;
using CommandDotNet;
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.Contains("[command]", text, StringComparison.Ordinal);
        Assert.Contains("open", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("convert", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fileformats", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugins", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["/?"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.Contains("[command]", text, StringComparison.Ordinal);
        Assert.Contains("open", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugins", text, StringComparison.OrdinalIgnoreCase);
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
    public void TryReadPluginPathFromAppSettings_ReadsPluginPath_FromSettingsCollection()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "SkyCD"));

        try
        {
            var expectedPath = Path.Combine(appDataRoot, "CustomPlugins");

            var configuration = new DatabaseConfiguration
            {
                Directory = Path.Combine(appDataRoot, "SkyCD")
            };

            using var database = new Database("skycd", configuration);
            var settings = database.CreateCollection("settings", Collection.DefaultScopeName);
            var appOptions = new MutableDocument("app-options");
            appOptions.SetString("pluginPath", expectedPath);
            settings.Save(appOptions);

            var resolved = CliHost.TryReadPluginPathFromAppSettings(appDataRoot);

            Assert.Equal(expectedPath, resolved);
        }
        finally
        {
            Directory.Delete(appDataRoot, recursive: true);
        }
    }

    [Fact]
    public void TryReadPluginPathFromAppSettings_FallsBackToLegacyJson_WhenSettingsCollectionMissing()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "SkyCD"));

        try
        {
            var expectedPath = Path.Combine(appDataRoot, "LegacyPlugins");
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
    public void TryReadPluginPathFromAppSettings_ReturnsNull_WhenNoSettingsExist()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-appdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "SkyCD"));

        try
        {
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["open", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.Contains("open [options]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--format", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["convert", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("convert [options]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--in", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--out", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--in-format", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["plugins", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("plugins [command]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["plugins", "/?"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("plugins [command]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["fileformats", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("fileformats [command]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RootHelp_UsesCommandDotNetUsageFormat()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.Contains("[command]", text, StringComparison.Ordinal);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["plugins"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("plugins [command]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", text, StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["fileformats"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("fileformats [command]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Plugins_WithoutSubcommand_WithJson_ShowsHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["plugins", "--json"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("plugins [command]", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FileFormats_WithoutSubcommand_WithJson_ShowsHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

        var result = await host.TryRunAsync(["fileformats", "--json"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("fileformats [command]", output.ToString(), StringComparison.OrdinalIgnoreCase);
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
            (_, _) => throw new InvalidOperationException("Runtime should not load for help."));

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
    public async Task Convert_WritesPayloadWithoutCliExtensions()
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
            Assert.Equal("seed", converted);
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
    public async Task PluginCommand_MissingCommandAttribute_ProducesConfigurationError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, [CreateSingleCapabilityPlugin("tests.cli.bad", new MissingCommandAttributeCapability())]);

        var result = await host.TryRunAsync(["tests"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.ConfigurationError, result.ExitCode);
        Assert.Contains("missing [Command(\"name\")] attribute", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static CliHost CreateHost(TextWriter stdout, TextWriter stderr, IEnumerable<DiscoveredPlugin> plugins)
    {
        var pluginList = plugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var services = new ServiceCollection()
            .AddRegistrator<CommonRuntimeServiceRegistrator>();

        services.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            services.AddPluginRegistrator(plugin);
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
                    new TestCliRootCommand()
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
                Capabilities = [new DuplicateCliRootCommand()]
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

    [Command("tests")]
    public sealed class TestCliRootCommand : ICliPluginCapability
    {
        [Subcommand]
        public TestGreetCliCommand Greet { get; } = new();
    }

    [Command("greet")]
    public sealed class TestGreetCliCommand
    {
        [DefaultCommand]
        public int Execute()
        {
            System.Console.WriteLine("hello from plugin");
            return 0;
        }
    }

    [Command("tests")]
    public sealed class DuplicateCliRootCommand : ICliPluginCapability
    {
        [Subcommand]
        public DuplicateGreetCliCommand Greet { get; } = new();
    }

    [Command("greet")]
    public sealed class DuplicateGreetCliCommand
    {
        [DefaultCommand]
        public int Execute()
        {
            System.Console.WriteLine("duplicate");
            return 0;
        }
    }

    [Command("open")]
    public sealed class ReservedCommandCapability : ICliPluginCapability
    {
        [DefaultCommand]
        public int Execute()
        {
            System.Console.WriteLine("reserved");
            return 0;
        }
    }

    private sealed class MissingCommandAttributeCapability : ICliPluginCapability
    {
    }
}
