using CommandDotNet;
using SkyCD.SDK.Capabilities.Cli;
using Xunit;

namespace SkyCD.Cli.Tests;

public sealed class CliJsonOutputTests
{
    [Command("sync")]
    private sealed class SyncStub : ICliPluginCapability
    {
        [Subcommand] public JetBrainsStub JetBrains { get; } = new();
    }

    [Command("jetbrains")]
    private sealed class JetBrainsStub : ICliPluginCapability, ISupportsJsonOutput
    {
        public bool JsonOutput { get; set; }
    }

    [Fact]
    public void ApplyJsonOutput_SetsFlagOnSupportingCommands()
    {
        var command = new JetBrainsStub();
        CliHost.ApplyJsonOutput(command, jsonOutput: true);
        Assert.True(command.JsonOutput);
    }

    [Fact]
    public void ApplyJsonOutput_IgnoresCommandsWithoutSupport()
    {
        var command = new SyncStub();
        CliHost.ApplyJsonOutput(command, jsonOutput: true);
        var exception = Record.Exception(() => CliHost.ApplyJsonOutput(new object(), jsonOutput: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Registry_ResolvesNestedPluginCommandPaths()
    {
        using var registry = new CliContributionRegistry();
        registry.Register([new SyncStub(), new JetBrainsStub()]);
        Assert.Contains("sync", registry.CommandPaths);
        Assert.Contains("jetbrains", registry.CommandPaths);
    }
}
