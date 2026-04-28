using Couchbase.Lite;
using System;
using System.IO;

namespace SkyCD.App.Services;

public sealed class CouchbaseLocalStore : IDisposable
{
    private const string DatabaseName = "skycd";
    public const string AppOptionsDocumentId = "app-options";

    private readonly Database _database;

    public string DatabaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SkyCD"
        );

    public DatabaseConfiguration Configuration =>
        new DatabaseConfiguration
        {
            Directory = DatabaseDirectory
        };

    public CouchbaseLocalStore()
    {
        Directory.CreateDirectory(Configuration.Directory);

        _database = new Database(DatabaseName, Configuration);
    }

    public void Dispose()
    {
        _database.Close();
        _database.Dispose();
    }

    public Collection GetCollection(LocalCollection collection)
    {
        return GetOrCreateCollection(collection);
    }

    private Collection GetOrCreateCollection(LocalCollection collection)
    {
        var collectionName = GetCollectionName(collection);
        return _database.GetCollection(collectionName, Collection.DefaultScopeName)
            ?? _database.CreateCollection(collectionName, Collection.DefaultScopeName);
    }

    private static string GetCollectionName(LocalCollection collection)
    {
        return collection.ToString().ToLower();
    }
}
