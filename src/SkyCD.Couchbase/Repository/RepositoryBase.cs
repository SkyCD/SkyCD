using System;
using System.Collections.Generic;
using System.Reflection;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Helpers;
using SkyCD.Couchbase.Mapping;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Repository;

/// <summary>
/// Non-generic repository base so infrastructure can hand every repository the host logger before it
/// initializes, without knowing the document type.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>
    /// Diagnostics that must not abort repository construction. The host assigns its logger through
    /// <see cref="Collections.RepositoryCollection"/>; a repository built by hand keeps the no-op logger.
    /// </summary>
    public ILogger Logger { get; internal set; } = NullLogger.Instance;
}

public abstract class RepositoryBase<TDocument> : RepositoryBase, IRepository<TDocument>
    where TDocument : class, new()
{
    public Type DocumentType { get; private set; } = null!;
    public string CollectionName { get; private set; } = string.Empty;
    public DocumentPropertyBinding IdProperty { get; private set; } = new("Id", null);
    public Collection Collection { get; private set; } = null!;

    public virtual void Initialize(Type documentType, string collectionName, Collection collection)
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

    public TDocument? Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var document = Collection.GetDocument(id);
        return document.FromDocument<TDocument>();
    }

    public TDocument GetOrCreate(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var existing = Get(id);
        if (existing is not null)
        {
            return existing;
        }

        var created = new TDocument();
        TryAssignId(created, id);
        return created;
    }

    protected TDocument GetOrCreate(string id, Action<TDocument> configureCreated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configureCreated);

        var existing = Get(id);
        if (existing is not null)
        {
            return existing;
        }

        var created = new TDocument();
        TryAssignId(created, id);
        configureCreated(created);
        return created;
    }

    public virtual void Save(string id, TDocument value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);

        using var document = value.ToMutableDocument(id);
        Collection.Save(document);
    }

    public virtual IReadOnlyList<TDocument> GetAll()
    {
        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(Collection));

        using var results = query.Execute();
        return ReadResults(results);
    }

    /// <summary>
    /// Materializes the rows of a <c>SelectResult.All()</c> query into documents. Rows are keyed by the
    /// collection name, so the same projection serves whole-collection and filtered queries.
    /// </summary>
    protected IReadOnlyList<TDocument> ReadResults(IResultSet results)
    {
        ArgumentNullException.ThrowIfNull(results);

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

    private void TryAssignId(TDocument document, string id)
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