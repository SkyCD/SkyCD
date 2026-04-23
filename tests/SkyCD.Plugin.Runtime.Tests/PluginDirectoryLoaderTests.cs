using System.Reflection;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Loading;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginDirectoryLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skycd-plugins-{Guid.NewGuid():N}");

    public PluginDirectoryLoaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void LoadFromDirectories_LoadsPluginFromManifest()
    {
        var pluginDir = Path.Combine(_root, "good");
        Directory.CreateDirectory(pluginDir);

        var manifest = new PluginManifest
        {
            Id = "tests.loader",
            Version = "1.0.0",
            MinHostVersion = "3.0.0",
            Assembly = Path.GetFileName(Assembly.GetExecutingAssembly().Location),
            Capabilities = ["tests"]
        };

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), JsonSerializer.Serialize(manifest));
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(pluginDir, manifest.Assembly), overwrite: true);

        var loader = new PluginDirectoryLoader();
        var result = loader.LoadFromDirectories([_root], new PluginLoadOptions { HostVersion = new Version(3, 0, 0) });

        Assert.Contains(result.Plugins, plugin => plugin.Plugin.Id == "tests.runtime.loader-plugin");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.IsError);
    }

    [Fact]
    public void LoadFromDirectories_SkipsIncompatiblePluginAndReportsDiagnostic()
    {
        var pluginDir = Path.Combine(_root, "incompatible");
        Directory.CreateDirectory(pluginDir);

        var manifest = new PluginManifest
        {
            Id = "tests.incompatible",
            Version = "1.0.0",
            MinHostVersion = "4.0.0",
            Assembly = "missing.dll",
            Capabilities = []
        };

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), JsonSerializer.Serialize(manifest));

        var loader = new PluginDirectoryLoader();
        var result = loader.LoadFromDirectories([_root], new PluginLoadOptions { HostVersion = new Version(3, 0, 0) });

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.PluginId == "tests.incompatible" &&
            diagnostic.Message.Contains("Skipped incompatible", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

}

public sealed class LoaderTestPlugin : IPlugin
{
    public string Id => "tests.runtime.loader-plugin";
    public string Name => "Loader Test Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);

}
