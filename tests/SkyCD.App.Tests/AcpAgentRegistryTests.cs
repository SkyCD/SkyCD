using System;
using System.IO;
using System.Text.Json;
using SkyCD.Plugin.Ai.Description;
using Xunit;
using SkyCD.Plugin.Ai.Description.Exceptions;
using SkyCD.Plugin.Ai.Description.Sources;
using SkyCD.TestSupport;

namespace SkyCD.App.Tests;

public sealed class AcpAgentRegistryTests
{
    private static string WriteRegistry(TestWorkspace workspace, object value) =>
        workspace.CreateFile(AcpAgentRegistry.FileName, JsonSerializer.Serialize(value));

    [Fact]
    public void LoadsCachedEntriesWithoutExecutingOrInstallingThem()
    {
        using var workspace = new TestWorkspace();
        var path = WriteRegistry(workspace, new
        {
            version = "1.0.0",
            agents = new object[]
            {
                new
                {
                    id = "agent-one",
                    name = "Agent One",
                    version = "2.1.0",
                    description = "Test agent",
                    distribution = new { npx = new { package = "agent-one@2.1.0", args = new[] { "--acp" } } },
                    unexpected = new { command = "should-not-run" }
                },
                new
                {
                    id = "agent-two",
                    name = "Agent Two",
                    version = "1.0.0",
                    distribution = new { uvx = new { package = "agent-two" } }
                }
            }
        });
        var registry = AcpAgentRegistry.Load(path);
        Assert.Equal(2, registry.Agents.Count);
        var agent = Assert.IsType<AcpRegistryAgent>(registry.Find("agent-one"));
        Assert.Equal("Agent One", agent.Name);
        Assert.Equal("2.1.0", agent.Version);
        Assert.Equal("npx", Assert.Single(agent.Distributions));
        Assert.Equal("npx agent-one@2.1.0 --acp", agent.LaunchHint);
        Assert.Contains("uvx agent-two", registry.Find("agent-two")!.LaunchHint);
        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void SkipsMalformedEntriesAndIgnoresUnknownFields()
    {
        using var workspace = new TestWorkspace();
        var path = WriteRegistry(workspace, new
        {
            version = "1.0.0",
            extra = "ignored",
            agents = new object[]
            {
                new { name = "No id", distribution = new { npx = new { package = "x" } } },
                new { id = "no-distribution", name = "No distribution", version = "1.0.0" },
                new { id = "bad-args", name = "Bad args", version = "1.0.0", distribution = new { npx = new { package = "ok", args = new object[] { 1, "fine" } } } },
                new { id = "binary-only", name = "Binary", version = "1.0.0", distribution = new { binary = new { } } }
            }
        });
        var registry = AcpAgentRegistry.Load(path);
        Assert.Equal(2, registry.Agents.Count);
        var packageAgent = registry.Find("bad-args")!;
        Assert.Equal("npx ok fine", packageAgent.LaunchHint);
        var binaryAgent = registry.Find("binary-only")!;
        Assert.Equal("binary", Assert.Single(binaryAgent.Distributions));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":\"2.0.0\",\"agents\":[]}")]
    [InlineData("{\"version\":\"1.0.0\"}")]
    public void RejectsInvalidRegistryDocuments(string content)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath(AcpAgentRegistry.FileName);
        File.WriteAllText(path, content);
        Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(path));
    }

    [Fact]
    public void MissingAndOversizedCachesAreReported()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(workspace.GetPath("missing.json")));
        var path = workspace.GetPath(AcpAgentRegistry.FileName);
        using (var stream = File.Create(path)) stream.SetLength(AcpAgentRegistry.MaximumBytes + 1);
        Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(path));
    }
}
