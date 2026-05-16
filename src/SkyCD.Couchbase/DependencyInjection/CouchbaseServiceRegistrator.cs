using System;
using System.Diagnostics;
using System.IO;
using DryIoc;

namespace SkyCD.Couchbase.DependencyInjection;

public sealed class CouchbaseServiceRegistrator
{
    private const string AppDirectoryName = "SkyCD";
    private const string DefaultDatabaseName = "default";
    private const string DatabaseDirectoryEnvironmentVariable = "SKYCD_DATABASE_DIRECTORY";

    public void RegisterServices(IRegistrator registrator)
    {
        registrator.RegisterDelegate<DatabaseManager>(static _ =>
        {
            var manager = new DatabaseManager();
            var databaseDirectory = ResolveDatabaseDirectory();

            Directory.CreateDirectory(databaseDirectory);
            manager.Connect(DefaultDatabaseName, databaseDirectory);

            return manager;
        }, Reuse.Singleton);
        registrator.Register<RepositoryManager>(Reuse.Singleton);
    }

    private static string ResolveDatabaseDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable(DatabaseDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath.Trim();
        }

        if (IsTestHostProcess())
        {
            return Path.Combine(Path.GetTempPath(), AppDirectoryName, "tests", Process.GetCurrentProcess().Id.ToString());
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDirectoryName);
    }

    private static bool IsTestHostProcess()
    {
        return Process.GetCurrentProcess().ProcessName.Contains("testhost", StringComparison.OrdinalIgnoreCase);
    }
}

