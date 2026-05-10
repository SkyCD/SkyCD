using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Exceptions;
using SkyCD.Couchbase.Repository;
using CblCollection = Couchbase.Lite.Collection;

namespace SkyCD.Couchbase.Collections;

internal sealed class RepositoryCollection(DatabaseCollection Databases) : IDictionary<Type, object>
{
    private readonly ConcurrentDictionary<Type, object> inner = new();

    private CouchbaseDocument GetDocumentMapping(Type type)
    {
        var mapping = type.GetCustomAttributes(typeof(CouchbaseDocument), inherit: true);
        if (mapping.Length == 0 || mapping[0] is not CouchbaseDocument documentMapping)
        {
            throw new CouchbaseDocumentAttributeMissingException(type);
        }

        return documentMapping;
    }

    private static Type GetRepositoryInterfaceType(Type documentType)
    {
        return typeof(IRepository<>).MakeGenericType(documentType);
    }

    private object CreateInstanceForRepository(Type repositoryType, Type documentType)
    {
        var concreteRepositoryType = repositoryType.IsGenericTypeDefinition
            ? repositoryType.MakeGenericType(documentType)
            : repositoryType;

        var instance = Activator.CreateInstance(concreteRepositoryType)
                       ?? throw new RepositoryConstructorInvalidException(concreteRepositoryType);
        var repositoryInterfaceType = GetRepositoryInterfaceType(documentType);

        if (!repositoryInterfaceType.IsInstanceOfType(instance))
        {
            throw new RepositoryConstructorInvalidException(concreteRepositoryType);
        }

        return instance;
    }

    private CblCollection GetOrCreate(string databaseName, string collectionName)
    {
        var database = Databases[databaseName];

        return database.GetCollection(collectionName, CblCollection.DefaultScopeName)
               ?? database.CreateCollection(collectionName, CblCollection.DefaultScopeName);
    }

    private static void InitializeRepository(
        object repository,
        Type documentType,
        string collectionName,
        CblCollection collection)
    {
        var repositoryInterfaceType = GetRepositoryInterfaceType(documentType);
        var initialize = repositoryInterfaceType.GetMethod(nameof(IRepository<object>.Initialize))
                         ?? throw new RepositoryConstructorInvalidException(repository.GetType());

        initialize.Invoke(repository, [documentType, collectionName, collection]);
    }

    public object GetOrAdd(Type key)
    {
        return inner.GetOrAdd(key, type =>
        {
            var documentMapping = GetDocumentMapping(type);
            var repository = CreateInstanceForRepository(documentMapping.RepositoryType, type);
            var collection = GetOrCreate(documentMapping.Database, documentMapping.CollectionName);
            InitializeRepository(
                repository: repository,
                documentType: type,
                collectionName: documentMapping.CollectionName,
                collection: collection);

            return repository;
        });
    }

    public object this[Type key]
    {
        get => inner[key];
        set => inner[key] = value;
    }

    public ICollection<Type> Keys => inner.Keys;
    public ICollection<object> Values => inner.Values;
    public int Count => inner.Count;
    public bool IsReadOnly => false;

    public void Add(Type key, object value)
    {
        if (!inner.TryAdd(key, value))
        {
            throw new DuplicateRepositoryKeyException(key);
        }
    }

    public bool ContainsKey(Type key)
    {
        return inner.ContainsKey(key);
    }

    public bool Remove(Type key)
    {
        return inner.TryRemove(key, out _);
    }

    public bool TryGetValue(Type key, out object value)
    {
        var found = inner.TryGetValue(key, out var repository);
        value = repository!;
        return found;
    }

    public void Add(KeyValuePair<Type, object> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        inner.Clear();
    }

    public bool Contains(KeyValuePair<Type, object> item)
    {
        return ((ICollection<KeyValuePair<Type, object>>)inner).Contains(item);
    }

    public void CopyTo(KeyValuePair<Type, object>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<Type, object>>)inner).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<Type, object> item)
    {
        return ((ICollection<KeyValuePair<Type, object>>)inner).Remove(item);
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        return inner.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}