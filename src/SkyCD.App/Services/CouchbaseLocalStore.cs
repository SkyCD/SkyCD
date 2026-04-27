using Couchbase.Lite;
using System;
using System.IO;

namespace SkyCD.App.Services;

public sealed class CouchbaseLocalStore : IDisposable
{
    public const string DatabaseName = "skycd";
    public const string CatalogCollectionName = "catalog";
    public const string SettingsCollectionName = "settings";
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
        CatalogCollection = database.GetCollection(CatalogCollectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(CatalogCollectionName, Collection.DefaultScopeName);
        SettingsCollection = database.GetCollection(SettingsCollectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(SettingsCollectionName, Collection.DefaultScopeName);
    }

    public void Dispose()
    {
        database.Close();
        database.Dispose();
    }
}
