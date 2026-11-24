using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Couchbase.Lite;

namespace SkyCD.Couchbase.Collections;

internal sealed class DatabaseCollection : IDictionary<string, Database>
{
    private readonly ConcurrentDictionary<string, Database> inner = new();

    public ICollection<string> Keys => inner.Keys;
    public ICollection<Database> Values => inner.Values;
    public int Count => inner.Count;
    public bool IsReadOnly => false;

    public void Clear()
    {
        foreach (var key in Keys.ToArray())
        {
            Remove(key);
        }
    }

    public Database this[string key]
    {
        get => inner[key];
        set => inner[key] = value;
    }

    public void Add(string databaseName, DatabaseConfiguration configuration)
    {
        Add(databaseName, new Database(databaseName, configuration));
    }

    public void Add(string databaseName, Database database)
    {
        var fullDirectoryPath = Path.GetFullPath(database.Config.Directory);

        Directory.CreateDirectory(fullDirectoryPath);
        inner.TryAdd(databaseName, database);
    }

    public bool ContainsKey(string key)
    {
        return inner.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        // Removing the entry is the only signal that the collection stops owning the native database, so the
        // handle has to be released here; leaving it to the finalizer can outlive a deleted database directory.
        if (!inner.TryRemove(key, out var database))
        {
            return false;
        }

        database.Dispose();
        return true;
    }

    public bool TryGetValue(string key, out Database value)
    {
        var found = inner.TryGetValue(key, out var database);
        value = database!;
        return found;
    }

    public void Add(KeyValuePair<string, Database> item)
    {
        Add(item.Key, item.Value);
    }

    public bool Contains(KeyValuePair<string, Database> item)
    {
        return ((ICollection<KeyValuePair<string, Database>>)inner).Contains(item);
    }

    public void CopyTo(KeyValuePair<string, Database>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<string, Database>>)inner).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, Database> item)
    {
        return ((ICollection<KeyValuePair<string, Database>>)inner).Remove(item);
    }

    public IEnumerator<KeyValuePair<string, Database>> GetEnumerator()
    {
        return inner.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}