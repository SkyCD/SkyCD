using Couchbase.Lite;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace SkyCD.App.Services;

public sealed class CouchbaseLocalStore : IDisposable
{
    public const string DatabaseName = "skycd";
    public const string AppOptionsDocumentId = "app-options";

    private readonly Database database;

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
        _ = GetOrCreateCollection(LocalCollection.Catalog);
        _ = GetOrCreateCollection(LocalCollection.Settings);
    }

    public void Dispose()
    {
        database.Close();
        database.Dispose();
    }

    public Collection GetCollection(LocalCollection collection)
    {
        return GetOrCreateCollection(collection);
    }

    private Collection GetOrCreateCollection(LocalCollection collection)
    {
        var collectionName = GetCollectionName(collection);
        return database.GetCollection(collectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(collectionName, Collection.DefaultScopeName);
    }

    private static string GetCollectionName(LocalCollection collection)
    {
        var enumMember = typeof(LocalCollection)
            .GetMember(collection.ToString())
            .FirstOrDefault()?
            .GetCustomAttribute<EnumMemberAttribute>();

        if (!string.IsNullOrWhiteSpace(enumMember?.Value))
        {
            return enumMember.Value;
        }

        throw new ArgumentOutOfRangeException(nameof(collection), collection, null);
    }
}
