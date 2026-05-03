using Microsoft.Extensions.DependencyInjection;

namespace SkyCD.Couchbase.DependencyInjection;

public sealed class CouchbaseServiceRegistrator : IServiceRegistrator
{
    private const string AppDirectoryName = "SkyCD";
    private const string DefaultDatabaseName = "default";
    private const string LocalStoreDatabaseName = "skycd";

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
            manager.Connect(LocalStoreDatabaseName, databaseDirectory);

            return manager;
        });
        services.AddSingleton<RepositoryManager>();
    }
}
