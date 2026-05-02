using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Host.Tests;

internal static class PluginManagerTestFactory
{
    public static PluginManager Create()
    {
        return new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger.Instance),
            new DiscoveredPluginFactory(),
            new PluginDocumentFactory(),
            CreateRepositoryManager());
    }

    private static RepositoryManager CreateRepositoryManager()
    {
        var databaseManager = new DatabaseManager();
        var directory = Path.Combine(Path.GetTempPath(), "SkyCD", "PluginHostTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        databaseManager.Connect("default", directory);
        return new RepositoryManager(databaseManager);
    }
}
