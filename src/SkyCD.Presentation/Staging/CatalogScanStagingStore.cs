using System;
using Couchbase.Lite;
using SkyCD.Couchbase;
using SkyCD.Documents;

namespace SkyCD.Presentation.Staging;

public static class CatalogScanStagingStore
{
    public static IStagingStore Create(Database? database = null)
    {
        if (database is not null)
        {
            return new CouchbaseScanStagingStore(database);
        }

        return new InMemoryScanStagingStore();
    }

    public static IStagingStore Create(RepositoryManager? repositoryManager)
    {
        if (repositoryManager is not null)
        {
            try
            {
                var repo = repositoryManager.For<CatalogDocument>();
                if (repo is SkyCD.Couchbase.Repository.RepositoryBase<CatalogDocument> baseRepo &&
                    baseRepo.Collection?.Database is { } db)
                {
                    return new CouchbaseScanStagingStore(db);
                }
            }
            catch
            {
                // Fall back to in-memory store if repository manager is not initialized with Couchbase
            }
        }

        return new InMemoryScanStagingStore();
    }
}
