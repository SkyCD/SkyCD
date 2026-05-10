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

        repositoryType ??= typeof(DefaultRepository<>);
        if (!IsValidRepositoryType(repositoryType))
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

    private static bool IsValidRepositoryType(Type repositoryType)
    {
        if (ImplementsRepositoryInterface(repositoryType))
        {
            return true;
        }

        if (!repositoryType.IsGenericTypeDefinition)
        {
            return false;
        }

        var genericArguments = repositoryType.GetGenericArguments();
        if (genericArguments.Length != 1)
        {
            return false;
        }

        try
        {
            var closed = repositoryType.MakeGenericType(typeof(object));
            return ImplementsRepositoryInterface(closed);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ImplementsRepositoryInterface(Type repositoryType)
    {
        foreach (var implemented in repositoryType.GetInterfaces())
        {
            if (!implemented.IsGenericType)
            {
                continue;
            }

            if (implemented.GetGenericTypeDefinition() == typeof(IRepository<>))
            {
                return true;
            }
        }

        return false;
    }
}