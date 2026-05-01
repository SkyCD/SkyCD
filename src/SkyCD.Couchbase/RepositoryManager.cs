using RepoBase = SkyCD.Couchbase.Repository.RepositoryBase;
using RepoCollection = SkyCD.Couchbase.Collections.RepositoryCollection;

namespace SkyCD.Couchbase;

public class RepositoryManager(DatabaseManager databaseManager)
{
    private readonly RepoCollection repositories = new(databaseManager.DatabasesCollection);

    public RepoBase For<TDocument>()
        where TDocument : class
    {
        return For(typeof(TDocument));
    }

    public RepoBase For(Type documentType)
    {
        return repositories.GetOrAdd(documentType);
    }
}
