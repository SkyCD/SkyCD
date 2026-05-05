using System;
using System.Collections.Generic;
using System.Reflection;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Mapping;

namespace SkyCD.Couchbase.Repository;

public abstract class RepositoryBase
{
    public Type DocumentType { get; private set; } = null!;
    public string CollectionName { get; private set; } = string.Empty;
    public Collection Collection { get; internal set; } = null!;

    internal void Initialize(Type documentType, string collectionName)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        DocumentType = documentType;
        CollectionName = collectionName;
    }

    public TDocument? Get<TDocument>(string id)
        where TDocument : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var document = Collection.GetDocument(id);
        return document.FromDocument<TDocument>();
    }

    public TDocument GetOrCreate<TDocument>(string id)
        where TDocument : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var existing = Get<TDocument>(id);
        if (existing is not null)
        {
            return existing;
        }

        var created = new TDocument();
        TryAssignId(created, id);
        return created;
    }

    public void Save<TDocument>(string id, TDocument value)
        where TDocument : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);

        using var document = value.ToMutableDocument(id);
        Collection.Save(document);
    }

    public IReadOnlyList<TDocument> GetAll<TDocument>()
        where TDocument : class, new()
    {
        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(Collection));

        using var results = query.Execute();
        var items = new List<TDocument>();

        foreach (var row in results)
        {
            var dictionary = row.GetDictionary(Collection.Name);
            if (dictionary is null)
            {
                continue;
            }

            using var document = new MutableDocument(dictionary.ToDictionary());
            var mapped = document.FromDocument<TDocument>();
            if (mapped is null)
            {
                continue;
            }

            items.Add(mapped);
        }

        return items;
    }

    private static void TryAssignId<TDocument>(TDocument document, string id)
        where TDocument : class
    {
        var idProperty = typeof(TDocument).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        if (idProperty is null || idProperty.PropertyType != typeof(string))
        {
            return;
        }

        var current = idProperty.GetValue(document) as string;
        if (!string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        try
        {
            idProperty.SetValue(document, id);
        }
        catch (ArgumentException)
        {
            // Unsupported setter signature for reflective assignment.
        }
        catch (TargetException)
        {
            // Not assignable in current runtime context.
        }
        catch (MethodAccessException)
        {
            // Setter exists but is not accessible.
        }
    }
}
