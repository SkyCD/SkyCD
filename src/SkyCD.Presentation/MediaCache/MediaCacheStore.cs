using System;
using Couchbase.Lite;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Creates the media cache store that matches the available catalog storage, mirroring how the scan
/// staging store is resolved. A catalog database gives durable, backup-portable storage; otherwise the
/// in-memory store keeps the same lifecycle semantics for the current session.
/// </summary>
public static class MediaCacheStore
{
    public static IMediaCacheStore Create(Database? database = null, MediaCacheRetentionPolicy? policy = null)
    {
        return database is not null
            ? new CouchbaseMediaCacheStore(database, policy)
            : new InMemoryMediaCacheStore(policy);
    }

    public static IMediaCacheStore Create(RepositoryManager? repositoryManager, MediaCacheRetentionPolicy? policy = null)
    {
        if (repositoryManager is not null)
        {
            try
            {
                if (repositoryManager.For<CatalogDocument>() is RepositoryBase<CatalogDocument> catalogRepository &&
                    catalogRepository.Collection?.Database is { } database)
                {
                    return new CouchbaseMediaCacheStore(database, policy);
                }
            }
            catch
            {
                // Fall back to the in-memory store when no Couchbase catalog is initialized.
            }
        }

        return new InMemoryMediaCacheStore(policy);
    }
}
