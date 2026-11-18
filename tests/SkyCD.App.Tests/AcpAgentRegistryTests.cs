using System;
using System.IO;
using System.Text.Json;
using SkyCD.Plugin.AiDescription;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class AcpAgentRegistryTests
{
    private static string WriteRegistry(object value)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-acp-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AcpAgentRegistry.FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }

    [Fact]
    public void LoadsCachedEntriesWithoutExecutingOrInstallingThem()
    {
        var path = WriteRegistry(new
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
        try
        {
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
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void SkipsMalformedEntriesAndIgnoresUnknownFields()
    {
        var path = WriteRegistry(new
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
        try
        {
            var registry = AcpAgentRegistry.Load(path);
            Assert.Equal(2, registry.Agents.Count);
            var packageAgent = registry.Find("bad-args")!;
            Assert.Equal("npx ok fine", packageAgent.LaunchHint);
            var binaryAgent = registry.Find("binary-only")!;
            Assert.Equal("binary", Assert.Single(binaryAgent.Distributions));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":\"2.0.0\",\"agents\":[]}")]
    [InlineData("{\"version\":\"1.0.0\"}")]
    public void RejectsInvalidRegistryDocuments(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-acp-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AcpAgentRegistry.FileName);
        try
        {
            File.WriteAllText(path, content);
            Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MissingAndOversizedCachesAreReported()
    {
        Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json")));
        var directory = Path.Combine(Path.GetTempPath(), "skycd-acp-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AcpAgentRegistry.FileName);
        try
        {
            using (var stream = File.Create(path)) stream.SetLength(AcpAgentRegistry.MaximumBytes + 1);
            Assert.Throws<AcpRegistryException>(() => AcpAgentRegistry.Load(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
