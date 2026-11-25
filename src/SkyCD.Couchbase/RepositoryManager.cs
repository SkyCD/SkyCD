using System;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase.Repository;
using RepoCollection = SkyCD.Couchbase.Collections.RepositoryCollection;

namespace SkyCD.Couchbase;

public class RepositoryManager
{
    private readonly RepoCollection repositories;

    public RepositoryManager(DatabaseManager databaseManager, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(databaseManager);
        repositories = new RepoCollection(databaseManager.DatabasesCollection, loggerFactory);
    }

    public IRepository<TDocument> For<TDocument>()
        where TDocument : class, new()
    {
        return (IRepository<TDocument>)For(typeof(TDocument));
    }

    internal object For(Type documentType)
    {
        return repositories.GetOrAdd(documentType);
    }
}