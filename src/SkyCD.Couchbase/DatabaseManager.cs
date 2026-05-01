using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Collections;
using System.Reflection;
using CblDatabase = Couchbase.Lite.Database;
using CblDatabaseConfiguration = Couchbase.Lite.DatabaseConfiguration;

namespace SkyCD.Couchbase;

public class DatabaseManager : IDisposable
{
    private const string DefaultConnectionKey = "default";
    private static readonly DatabaseCollection LocalDatabases = new();
    
    internal DatabaseCollection DatabasesCollection => LocalDatabases;

    public static CblDatabase Connect(string databaseName, CblDatabaseConfiguration configuration)
    {
        LocalDatabases.Add(databaseName, configuration);
        
        return LocalDatabases[databaseName];
    }
    
    public CblDatabase this[string key]
    {
        get => LocalDatabases[key];
        set => LocalDatabases[key] = value;
    }

    public static CblDatabase GetDatabase(string connectionKey = DefaultConnectionKey)
    {
        return LocalDatabases[connectionKey];
    }

    public CblDatabase Connect(string databaseName, string directoryPath)
    {
        LocalDatabases.Add(databaseName, new CblDatabaseConfiguration
        {
            Directory = directoryPath
        });
        
        return LocalDatabases[databaseName];
    }

    public bool Disconnect(string databaseName)
    {
        return LocalDatabases.Remove(databaseName);
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
        LocalDatabases.Clear();
    }
}
