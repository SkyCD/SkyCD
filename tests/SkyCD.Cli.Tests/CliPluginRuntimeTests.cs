using SkyCD.Cli;

using System.Text.Json;

namespace SkyCD.Cli.Tests;

public sealed class CliPluginRuntimeTests
{
    [Fact]
    public void BuildPluginDirectories_UsesConfiguredAndAppSettingsPaths_AndDeduplicates()
    {
        var first = Path.Combine(Path.GetTempPath(), "skycd-cli-runtime-first");
        var second = Path.Combine(Path.GetTempPath(), "skycd-cli-runtime-second");
        var configured = string.Join(Path.PathSeparator, [first, second, first]);

        var directories = CliPluginRuntime.BuildPluginDirectories(configured, second);

        Assert.Equal(2, directories.Count);
        Assert.Contains(Path.GetFullPath(first), directories, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(second), directories, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPluginDirectories_DoesNotAddLocalFallbackDirectories()
    {
        var directories = CliPluginRuntime.BuildPluginDirectories(null, null);

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

            var resolved = CliPluginRuntime.TryReadPluginPathFromAppSettings(appDataRoot);

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
            Assert.Null(CliPluginRuntime.TryReadPluginPathFromAppSettings(appDataRoot));

            var optionsPath = Path.Combine(appDataRoot, "SkyCD", "options.json");
            File.WriteAllText(optionsPath, "{ not json");
            Assert.Null(CliPluginRuntime.TryReadPluginPathFromAppSettings(appDataRoot));
        }
        finally
        {
            Directory.Delete(appDataRoot, recursive: true);
        }
    }
}
