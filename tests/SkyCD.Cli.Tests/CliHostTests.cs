using SkyCD.Cli;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
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
    public async Task ListFormatsAlias_ResolvesToFileFormatsList()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, CreateTestPlugins());

        var result = await host.TryRunAsync(["list-formats"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("tests-read", text, StringComparison.Ordinal);
        Assert.Contains("tests-write", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ListFormatsAlias_Help_ShowsFileFormatsListUsage()
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
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        var text = output.ToString();
        Assert.Contains("renamed-cli.exe fileformats list [--json]", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
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

    private static CliHost CreateHost(TextWriter stdout, TextWriter stderr, IEnumerable<DiscoveredPlugin> plugins)
    {
        return new CliHost(
            stdout,
            stderr,
            (_, _) => Task.FromResult(new CliPluginRuntime
            {
                DiscoveredPlugins = plugins.ToList(),
                Diagnostics = [],
                PluginDirectories = []
            }));
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
}
