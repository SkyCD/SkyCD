using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SkyCD.Cli.Enum;
using SkyCD.Cli.Mcp;
using Xunit;

namespace SkyCD.Cli.Tests;

public sealed class CliMcpBridgeTests
{
    [Fact]
    public async Task ListTools_IncludesBuiltInCommands()
    {
        var bridge = new CliMcpBridge();

        var tools = await bridge.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name.Equals("skycd.open", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, tool => tool.Name.Equals("skycd.convert", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, tool => tool.Name.Equals("skycd.fileformats.list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, tool => tool.Name.Equals("skycd.plugins.list", StringComparison.OrdinalIgnoreCase));
        Assert.All(tools, tool => Assert.StartsWith("http://127.0.0.1:", tool.Url, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvokeTool_Open_ReturnsStructuredSuccess()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"skycd-mcp-open-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var inputPath = Path.Combine(tempDirectory, "catalog.json");
            await File.WriteAllTextAsync(
                inputPath,
                """
                {
                  "schemaVersion": "skycd.catalog.v1",
                  "payload": []
                }
                """,
                Encoding.UTF8);

            var bridge = new CliMcpBridge();
            var result = await bridge.InvokeToolAsync("skycd.open", new Dictionary<string, JsonNode?>
            {
                ["file"] = inputPath,
                ["format"] = "skycd-json"
            });

            Assert.True(result.Success);
            Assert.Equal((int)CliExitCodes.Success, result.ExitCode);
            Assert.NotNull(result.Data);
            var successNode = result.Data!["success"];
            if (successNode is not null)
            {
                Assert.True(successNode.GetValue<bool>());
                Assert.Equal("open", result.Data["command"]?.GetValue<string>());
            }
            else
            {
                var rawOutput = result.Data["rawOutput"]?.GetValue<string>() ?? string.Empty;
                Assert.True(
                    rawOutput.Contains("Opened", StringComparison.OrdinalIgnoreCase)
                    || rawOutput.Contains("\"success\": true", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeTool_UnknownTool_ReturnsNormalizedError()
    {
        var bridge = new CliMcpBridge();

        var result = await bridge.InvokeToolAsync("skycd.unknown");

        Assert.False(result.Success);
        Assert.Equal((int)CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown tool", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
