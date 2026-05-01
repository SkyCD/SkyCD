using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Repositories;
using System.Collections.Concurrent;
using System.Collections;
using SkyCD.Couchbase.Repository;
using CblCollection = Couchbase.Lite.Collection;

namespace SkyCD.Couchbase.Collections;

internal sealed class RepositoryCollection(DatabaseCollection Databases) : IDictionary<Type, RepositoryBase>
{
    private readonly ConcurrentDictionary<Type, RepositoryBase> inner = new();

    private CouchbaseDocument GetDocumentMapping(Type type)
    {
        var mapping = type.GetCustomAttributes(typeof(CouchbaseDocument), inherit: true);
        if (mapping.Length == 0 || mapping[0] is not CouchbaseDocument documentMapping)
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' must be annotated with [CouchbaseDocument(\"collection\")].");
        }
        
        return documentMapping;
    }

    private RepositoryBase CreateInstanceForRepository(Type type)
    {
        var instance = Activator.CreateInstance(type);

        if (instance is not RepositoryBase @base)
        {
            throw new InvalidOperationException(
                $"Repository type '{type.FullName}' must have a public parameterless constructor.");
        }

        return @base;
    }

    private CblCollection GetOrCreate(string databaseName, string collectionName)
    {
        var database = Databases[databaseName];

        return database.GetCollection(collectionName, CblCollection.DefaultScopeName)
               ?? database.CreateCollection(collectionName, CblCollection.DefaultScopeName);
    }
    
    public RepositoryBase GetOrAdd(Type key)
    {
        return inner.GetOrAdd(key, type =>
        {
            var documentMapping = GetDocumentMapping(type);
            var repository = CreateInstanceForRepository(documentMapping.RepositoryType);
            
            repository.Initialize(type, documentMapping.CollectionName);
            repository.Collection = GetOrCreate(documentMapping.Database, documentMapping.CollectionName);
            
            return repository;
        });
    }

    public RepositoryBase this[Type key]
    {
        get => inner[key];
        set => inner[key] = value;
    }

    public ICollection<Type> Keys => inner.Keys;
    public ICollection<RepositoryBase> Values => inner.Values;
    public int Count => inner.Count;
    public bool IsReadOnly => false;

    public void Add(Type key, RepositoryBase value)
    {
        if (!inner.TryAdd(key, value))
        {
            throw new ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));
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

    public bool TryGetValue(Type key, out RepositoryBase value)
    {
        var found = inner.TryGetValue(key, out var repository);
        value = repository!;
        return found;
    }

    public void Add(KeyValuePair<Type, RepositoryBase> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        inner.Clear();
    }

    public bool Contains(KeyValuePair<Type, RepositoryBase> item)
    {
        return ((ICollection<KeyValuePair<Type, RepositoryBase>>)inner).Contains(item);
    }

    public void CopyTo(KeyValuePair<Type, RepositoryBase>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<Type, RepositoryBase>>)inner).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<Type, RepositoryBase> item)
    {
        return ((ICollection<KeyValuePair<Type, RepositoryBase>>)inner).Remove(item);
    }

    public IEnumerator<KeyValuePair<Type, RepositoryBase>> GetEnumerator()
    {
        return inner.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
