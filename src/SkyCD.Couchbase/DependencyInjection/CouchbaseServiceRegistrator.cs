using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace SkyCD.Couchbase.DependencyInjection;

public sealed class CouchbaseServiceRegistrator
{
    private const string AppDirectoryName = "SkyCD";
    private const string DefaultDatabaseName = "default";

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseManager>(static _ =>
        {
            var manager = new DatabaseManager();
            var databaseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDirectoryName);

            Directory.CreateDirectory(databaseDirectory);
            manager.Connect(DefaultDatabaseName, databaseDirectory);

            return manager;
        });
        services.AddSingleton<RepositoryManager>();
    }
}
