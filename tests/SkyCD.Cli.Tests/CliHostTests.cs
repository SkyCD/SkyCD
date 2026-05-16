using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Enum;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;

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
            error);

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
            error);

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
    public void TryReadPluginPathFromAppSettings_ReturnsInstalledDefaultPath()
    {
        var resolved = CliHost.TryReadPluginPathFromAppSettings();
        Assert.NotNull(resolved);
        Assert.EndsWith("Plugins", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void TryReadPluginPathFromAppSettings_IgnoresEnvironmentVariable()
    {
        var previousValue = Environment.GetEnvironmentVariable("SKYCD_PLUGIN_PATH");

        try
        {
            var envPath = Path.Combine(Path.GetTempPath(), "EnvPlugins");
            Environment.SetEnvironmentVariable("SKYCD_PLUGIN_PATH", envPath);
            var resolved = CliHost.TryReadPluginPathFromAppSettings();

            Assert.NotNull(resolved);
            Assert.EndsWith("Plugins", resolved, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(resolved));
            Assert.NotEqual(Path.GetFullPath(envPath), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SKYCD_PLUGIN_PATH", previousValue);
        }
    }

    [Fact]
    public async Task OpenHelp_ShowsCommandSpecificOptions()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error);

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
            error);

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
            error);

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
            error);

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
            error);

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
        var host = new CliHost(output, error);

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
            error);

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
            error);

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
            error);

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
            error);

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
        var host = new CliHost(output, error);

        var result = await host.TryRunAsync(["list-formats"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'list-formats'. Did you mean 'fileformats list'?", error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListFormatsAlias_Help_IsRejectedWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error);

        var result = await host.TryRunAsync(["list-formats", "--help"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'list-formats'. Did you mean 'fileformats list'?", error.ToString(),
            StringComparison.Ordinal);
    }


    [Fact]
    public async Task PluginsList_AsConcatenatedToken_ReturnsInvalidArgumentsWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error);

        var result = await host.TryRunAsync(["pluginslist"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'pluginslist'. Did you mean 'plugins list'?", error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileFormatsList_AsConcatenatedToken_ReturnsInvalidArgumentsWithHint()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error);

        var result = await host.TryRunAsync(["fileformatslist"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains("Unknown command 'fileformatslist'. Did you mean 'fileformats list'?", error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_ValidJsonFile_ReturnsSuccess()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"skycd-cli-open-{Guid.NewGuid():N}");
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

            var output = new StringWriter();
            var error = new StringWriter();
            var host = new CliHost(output, error);

            var result = await host.TryRunAsync(["open", inputPath, "--format", "skycd-json"]);

            Assert.True(result.Handled);
            Assert.Equal(CliExitCodes.Success, result.ExitCode);
            Assert.Contains("Opened", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Convert_JsonToCsv_ReturnsSuccess()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"skycd-cli-convert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var inputPath = Path.Combine(tempDirectory, "catalog.json");
            var outputPath = Path.Combine(tempDirectory, "catalog.csv");
            await File.WriteAllTextAsync(
                inputPath,
                """
                {
                  "schemaVersion": "skycd.catalog.v1",
                  "payload": [
                    {
                      "nodeId": "library",
                      "parentId": "",
                      "kind": "Folder",
                      "name": "Library",
                      "sizeBytes": "0"
                    }
                  ]
                }
                """,
                Encoding.UTF8);

            var output = new StringWriter();
            var error = new StringWriter();
            var host = new CliHost(output, error);

            var result = await host.TryRunAsync([
                "convert", "--in", inputPath, "--out", outputPath, "--in-format", "skycd-json", "--format", "skycd-csv"
            ]);

            Assert.True(result.Handled);
            Assert.Equal(CliExitCodes.Success, result.ExitCode);
            Assert.True(File.Exists(outputPath));
            var converted = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.Contains("NodeId,ParentId,Kind,Name,SizeBytes", converted, StringComparison.Ordinal);
            Assert.DoesNotContain("Command failed", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PluginsList_ReturnsSuccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error);

        var result = await host.TryRunAsync(["plugins", "list"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("Plugin directories checked:", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task FileFormatsList_ReturnsSuccess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error);

        var result = await host.TryRunAsync(["fileformats", "list"]);

        Assert.True(result.Handled);
        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        Assert.Contains("skycd-json", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }
}
