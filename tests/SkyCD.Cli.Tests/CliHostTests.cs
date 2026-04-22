using SkyCD.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;
using System.Text;

namespace SkyCD.Cli.Tests;

public sealed class CliHostTests
{
    [Fact]
    public async Task PluginCommand_Executes_WithoutLaunchingUiPath()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var plugin = new TestPlugin();
        var host = CreateHost(output, error, [plugin]);

        var result = await host.TryRunAsync(["tests greet"]);

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
            var plugin = new TestPlugin();
            var host = CreateHost(output, error, [plugin]);

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
    public async Task DuplicatePluginCommand_ReturnsConfigurationError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = CreateHost(output, error, [new TestPlugin(), new DuplicateCommandPlugin()]);

        var result = await host.TryRunAsync(["tests greet"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.ConfigurationError, result.ExitCode);
        Assert.Contains("CLI command collision", error.ToString(), StringComparison.Ordinal);
    }

    private static CliHost CreateHost(TextWriter stdout, TextWriter stderr, IEnumerable<IPlugin> plugins)
    {
        return new CliHost(
            stdout,
            stderr,
            (_, _) => Task.FromResult(new CliPluginRuntime
            {
                DiscoveredPlugins = plugins.Select(ToDiscoveredPlugin).ToList(),
                Diagnostics = []
            }));
    }

    private static DiscoveredPlugin ToDiscoveredPlugin(IPlugin plugin)
    {
        var capabilities = new List<SkyCD.Plugin.Abstractions.Capabilities.IPluginCapability>();
        if (plugin is IFileFormatPluginCapability fileFormat)
        {
            capabilities.Add(fileFormat);
        }

        if (plugin is ICliPluginCapability cli &&
            capabilities.All(existing => !ReferenceEquals(existing, cli)))
        {
            capabilities.Add(cli);
        }

        return new DiscoveredPlugin
        {
            Plugin = plugin,
            Capabilities = capabilities
        };
    }

    private sealed class TestPlugin : IPlugin, IFileFormatPluginCapability, ICliPluginCapability
    {
        public PluginDescriptor Descriptor => new("tests.cli", "Tests CLI", new Version(1, 0, 0), new Version(3, 0, 0));

        public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
        [
            new("tests-read", "Tests Read", [".src"], CanRead: true, CanWrite: false),
            new("tests-write", "Tests Write", [".dst"], CanRead: false, CanWrite: true)
        ];

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public IReadOnlyCollection<CliCommandContribution> GetCliContributions() =>
        [
            new CliCommandContribution("tests greet", "greet", "Greet command"),
            new CliCommandContribution("convert", "convert-ext", "Convert extension", CliContributionType.Extension, Priority: 10)
        ];

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return context.CommandId switch
            {
                "greet" => Task.FromResult(new CliCommandResult
                {
                    Success = true,
                    Output = "hello from plugin"
                }),
                "convert-ext" => Task.FromResult(new CliCommandResult
                {
                    Success = true,
                    Payload = $"{context.Payload}|ext"
                }),
                _ => Task.FromResult(new CliCommandResult
                {
                    Success = false,
                    Error = "Unknown command id"
                })
            };
        }

        public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            return new FileFormatReadResult
            {
                Success = true,
                Payload = await reader.ReadToEndAsync(cancellationToken)
            };
        }

        public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(request.Target, Encoding.UTF8, leaveOpen: true);
            await writer.WriteAsync(Convert.ToString(request.Payload));
            await writer.FlushAsync(cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
    }

    private sealed class DuplicateCommandPlugin : IPlugin, ICliPluginCapability
    {
        public PluginDescriptor Descriptor => new("tests.cli.dup", "Tests CLI Dup", new Version(1, 0, 0), new Version(3, 0, 0));

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public IReadOnlyCollection<CliCommandContribution> GetCliContributions() =>
        [
            new CliCommandContribution("tests greet", "greet", "Conflicting command")
        ];

        public Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CliCommandResult
            {
                Success = true
            });
        }
    }
}
