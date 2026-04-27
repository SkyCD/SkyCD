using Couchbase.Lite;
using System;
using System.IO;

namespace SkyCD.App.Services;

public sealed class CouchbaseLocalStore : IDisposable
{
    public enum LocalCollection
    {
        Catalog,
        Settings
    }

    public const string DatabaseName = "skycd";
    public const string AppOptionsDocumentId = "app-options";

    private readonly Database database;

    public Collection CatalogCollection { get; }

    public Collection SettingsCollection { get; }

    public string DatabaseDirectory { get; }

    public CouchbaseLocalStore(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DatabaseDirectory = Path.Combine(root, "SkyCD");
        Directory.CreateDirectory(DatabaseDirectory);

        var configuration = new DatabaseConfiguration
        {
            Directory = DatabaseDirectory
        };

        database = new Database(DatabaseName, configuration);
        CatalogCollection = GetOrCreateCollection(LocalCollection.Catalog);
        SettingsCollection = GetOrCreateCollection(LocalCollection.Settings);
    }

    public void Dispose()
    {
        database.Close();
        database.Dispose();
    }

    private Collection GetOrCreateCollection(LocalCollection collection)
    {
        var collectionName = GetCollectionName(collection);
        return database.GetCollection(collectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(collectionName, Collection.DefaultScopeName);
    }

    private static string GetCollectionName(LocalCollection collection)
    {
        return collection switch
        {
            LocalCollection.Catalog => "catalog",
            LocalCollection.Settings => "settings",
            _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, null)
        };
    }
}
