using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginManagerLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skycd-plugins-{Guid.NewGuid():N}");

    public PluginManagerLoadingTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void LoadFromDirectories_LoadsPluginFromAssemblyWithoutManifest()
    {
        var pluginDir = Path.Combine(_root, "good");
        Directory.CreateDirectory(pluginDir);

        var assemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(pluginDir, assemblyName), overwrite: true);

        var pluginManager = new PluginManager();
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        Assert.DoesNotContain(pluginManager.Diagnostics, diagnostic => diagnostic.IsError);
    }

    [Fact]
    public void LoadFromDirectories_ReportsDiagnosticWhenAssemblyLoadFails()
    {
        var pluginDir = Path.Combine(_root, "invalid");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "broken.dll"), "this is not a valid .NET assembly");

        var pluginManager = new PluginManager();
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Empty(pluginManager.Plugins);
        Assert.Contains(pluginManager.Diagnostics, diagnostic =>
            diagnostic.PluginId == "<assembly-scan>" &&
            diagnostic.Message.Contains("broken.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromDirectories_ReportsDiagnosticWhenDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(_root, "missing");
        var pluginManager = new PluginManager();

        pluginManager.Discover(missingDirectory, new Version(3, 0, 0));

        Assert.Empty(pluginManager.Plugins);
        Assert.Contains(pluginManager.Diagnostics, diagnostic =>
            diagnostic.PluginId == "<directory>" &&
            diagnostic.Message.Contains(missingDirectory, StringComparison.OrdinalIgnoreCase));
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
