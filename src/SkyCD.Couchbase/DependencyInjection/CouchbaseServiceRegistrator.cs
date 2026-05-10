using System;
using System.IO;
using DryIoc;
using SkyCD.Plugin.Runtime.DependencyInjection;

namespace SkyCD.Couchbase.DependencyInjection;

public sealed class CouchbaseServiceRegistrator : IServiceRegistrator
{
    private const string AppDirectoryName = "SkyCD";
    private const string DefaultDatabaseName = "default";

    public void RegisterServices(IRegistrator registrator)
    {
        registrator.RegisterDelegate<DatabaseManager>(static _ =>
        {
            var manager = new DatabaseManager();
            var databaseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDirectoryName);

            Directory.CreateDirectory(databaseDirectory);
            manager.Connect(DefaultDatabaseName, databaseDirectory);

            return manager;
        }, Reuse.Singleton);
        registrator.Register<RepositoryManager>(Reuse.Singleton);
    }
}
