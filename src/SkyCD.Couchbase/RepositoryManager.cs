using System;
using SkyCD.Couchbase.Repository;
using RepoCollection = SkyCD.Couchbase.Collections.RepositoryCollection;

namespace SkyCD.Couchbase;

public class RepositoryManager(DatabaseManager databaseManager)
{
    private readonly RepoCollection repositories = new(databaseManager.DatabasesCollection);

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