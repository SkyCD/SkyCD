using System;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Couchbase.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CouchbaseDocument : Attribute
{
    public CouchbaseDocument(string collectionName, Type? repositoryType = null, string? database = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or whitespace.", nameof(collectionName));
        }

        repositoryType ??= typeof(DefaultRepository);
        if (!typeof(RepositoryBase).IsAssignableFrom(repositoryType))
        {
            throw new ArgumentException(
                $"Repository type must inherit {nameof(RepositoryBase)}.",
                nameof(repositoryType)
            );
        }

        CollectionName = collectionName;
        RepositoryType = repositoryType;
        Database = string.IsNullOrWhiteSpace(database) ? "default" : database;
    }

    public string CollectionName { get; }
    public Type RepositoryType { get; }
    public string Database { get; }
}
