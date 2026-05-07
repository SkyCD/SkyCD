using System;
using System.Collections.Generic;
using System.Reflection;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Helpers;
using SkyCD.Couchbase.Mapping;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Repository;

public abstract class RepositoryBase
{
    public Type DocumentType { get; private set; } = null!;
    public string CollectionName { get; private set; } = string.Empty;
    public DocumentPropertyBinding IdProperty { get; internal set; } = new("Id", null);
    public Collection Collection { get; internal set; } = null!;

    internal virtual void Initialize(Type documentType, string collectionName, Collection collection)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentNullException.ThrowIfNull(collection);

        DocumentType = documentType;
        CollectionName = collectionName;
        Collection = collection;
        IdProperty = AttributeHelper.ResolveStringPropertyWithAttributeOrDefault(
            documentType: documentType,
            attributeType: typeof(Id),
            defaultPropertyName: "Id");
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

    private void TryAssignId<TDocument>(TDocument document, string id)
        where TDocument : class
    {
        if (IdProperty.Property is null)
        {
            return;
        }

        var current = IdProperty.Property.GetValue(document) as string;
        if (!string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        try
        {
            IdProperty.Property.SetValue(document, id);
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
