using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Collections;
using System.Reflection;
using CblDatabase = Couchbase.Lite.Database;
using CblDatabaseConfiguration = Couchbase.Lite.DatabaseConfiguration;

namespace SkyCD.Couchbase;

public class DatabaseManager : IDisposable
{
    private const string DefaultConnectionKey = "default";
    private readonly DatabaseCollection localDatabases = new();

    internal DatabaseCollection DatabasesCollection => localDatabases;

    public CblDatabase Connect(string databaseName, CblDatabaseConfiguration configuration)
    {
        localDatabases.Add(databaseName, configuration);

        return localDatabases[databaseName];
    }

    public CblDatabase this[string key]
    {
        get => localDatabases[key];
        set => localDatabases[key] = value;
    }

    public CblDatabase GetDatabase(string connectionKey = DefaultConnectionKey)
    {
        return localDatabases[connectionKey];
    }

    public CblDatabase Connect(string databaseName, string directoryPath)
    {
        localDatabases.Add(databaseName, new CblDatabaseConfiguration
        {
            Directory = directoryPath
        });

        return localDatabases[databaseName];
    }

    public bool Disconnect(string databaseName)
    {
        return localDatabases.Remove(databaseName);
    }

    public CblDatabase GetFor<TDocument>()
        where TDocument : class
    {
        return GetFor(typeof(TDocument));
    }

    public CblDatabase GetFor(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        var mapping = documentType.GetCustomAttribute<CouchbaseDocument>()
            ?? throw new InvalidOperationException(
                $"Type '{documentType.FullName}' must be annotated with [CouchbaseDocument(\"collection\")].");

        var connectionKey = string.IsNullOrWhiteSpace(mapping.Database) ? "default" : mapping.Database;
        return GetDatabase(connectionKey);
    }

    public void Dispose()
    {
        localDatabases.Clear();
    }
}
