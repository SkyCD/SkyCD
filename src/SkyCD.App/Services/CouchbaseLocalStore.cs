using Couchbase.Lite;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Mapping;
using SkyCD.Couchbase.Repositories;
using System;
using System.IO;
using SkyCD.Couchbase.Repository;

namespace SkyCD.App.Services;

public sealed class CouchbaseLocalStore : IDisposable
{
    private const string DatabaseName = "skycd";

    private readonly DatabaseManager _databaseManager;
    private readonly RepositoryManager _repositoryManager;
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
        : this(new DatabaseManager())
    {
    }

    public CouchbaseLocalStore(DatabaseManager databaseManager)
    {
        _databaseManager = databaseManager;
        _repositoryManager = new RepositoryManager(_databaseManager);
        Directory.CreateDirectory(Configuration.Directory);
        _database = _databaseManager.Connect(DatabaseName, Configuration.Directory);
    }

    public void Dispose()
    {
        _database.Close();
        _database.Dispose();
    }

    public RepositoryBase GetRepository<TDocument>()
        where TDocument : class
    {
        var repository = _repositoryManager.For<TDocument>();
        var collection = _database.GetCollection(repository.CollectionName, Collection.DefaultScopeName)
            ?? _database.CreateCollection(repository.CollectionName, Collection.DefaultScopeName);
        repository.Collection = collection;
        return repository;
    }

    public RepositoryBase GetRepository(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        var repository = _repositoryManager.For(documentType);
        var collection = _database.GetCollection(repository.CollectionName, Collection.DefaultScopeName)
            ?? _database.CreateCollection(repository.CollectionName, Collection.DefaultScopeName);
        repository.Collection = collection;
        return repository;
    }
}
