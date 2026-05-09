using System;
using SkyCD.Couchbase.Exceptions;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Couchbase.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CouchbaseDocument : Attribute
{
    public CouchbaseDocument(string collectionName, Type? repositoryType = null, string? database = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new CouchbaseCollectionNameInvalidException();
        }

        repositoryType ??= typeof(DefaultRepository);
        if (!typeof(RepositoryBase).IsAssignableFrom(repositoryType))
        {
            throw new CouchbaseRepositoryTypeInvalidException();
        }

        CollectionName = collectionName;
        RepositoryType = repositoryType;
        Database = string.IsNullOrWhiteSpace(database) ? "default" : database;
    }

    public string CollectionName { get; }
    public Type RepositoryType { get; }
    public string Database { get; }
}
