using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Helpers;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Repository;

/// <summary>
/// Tree-aware repository helpers for documents that expose string id properties configured at repository creation.
/// Child lookups use an indexed query on the parent property, and the whole-collection snapshot is cached so a
/// single navigation reuses one read until the collection changes.
/// </summary>
public class TreeRepository<TDocument> : RepositoryBase<TDocument>
    where TDocument : class, new()
{
    private const string ParentIndexPrefix = "idx_tree_parent_";

    private readonly object snapshotLock = new();
    private IReadOnlyList<TDocument>? snapshot;
    private ListenerToken? changeListener;

    public DocumentPropertyBinding ParentIdProperty { get; internal set; } = new("ParentId", null);

    public override void Initialize(Type documentType, string collectionName, Collection collection)
    {
        base.Initialize(documentType, collectionName, collection);

        ParentIdProperty = AttributeHelper.ResolveStringPropertyWithAttributeOrDefault(
            documentType: documentType,
            attributeType: typeof(ParentId),
            defaultPropertyName: "ParentId");

        EnsureParentIndex();
        EnsureChangeListener();
    }

    public override void Save(string id, TDocument value)
    {
        base.Save(id, value);
        InvalidateSnapshot();
    }

    /// <summary>
    /// Returns the cached whole-collection snapshot, loading it once per collection change. Callers treat the
    /// documents as read-only projections; writes go through <see cref="Save"/> or explicitly invalidate.
    /// </summary>
    public override IReadOnlyList<TDocument> GetAll()
    {
        // The gate is taken before the snapshot lock so a reload never waits for the gate while holding a
        // lock that a gated writer (which invalidates the snapshot) needs.
        return DatabaseGate.Run(Collection.Database, () =>
        {
            lock (snapshotLock)
            {
                return snapshot ??= base.GetAll();
            }
        });
    }

    /// <summary>Drops the cached snapshot after a bulk write that bypassed <see cref="Save"/>.</summary>
    public void InvalidateSnapshot()
    {
        lock (snapshotLock)
        {
            snapshot = null;
        }
    }

    public IReadOnlyList<TDocument> GetRoots()
    {
        if (ParentIdProperty.Property is null)
        {
            return [];
        }

        var parent = Expression.Property(ParentIdProperty.Name);
        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(Collection))
            .Where(parent.IsNotValued().Or(parent.EqualTo(Expression.String(string.Empty))));

        return DatabaseGate.Run(Collection.Database, () =>
        {
            using var results = query.Execute();
            return ReadResults(results);
        });
    }

    /// <summary>
    /// Returns the children of one parent with an indexed query on <see cref="ParentIdProperty"/>, so the cost
    /// follows the size of the folder instead of the size of the catalog.
    /// </summary>
    public virtual IReadOnlyList<TDocument> GetChildrenOf(string parentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);

        if (ParentIdProperty.Property is null)
        {
            return [];
        }

        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(Collection))
            .Where(Expression.Property(ParentIdProperty.Name).EqualTo(Expression.String(parentId)));

        return DatabaseGate.Run(Collection.Database, () =>
        {
            using var results = query.Execute();
            return ReadResults(results);
        });
    }

    public IReadOnlyList<TDocument> GetDescendantsOf(string parentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        return GetDescendantsOf([parentId]);
    }

    public IReadOnlyList<TDocument> GetDescendantsOf(IEnumerable<string> parentIds)
    {
        ArgumentNullException.ThrowIfNull(parentIds);

        var parentIdSet = new HashSet<string>(
            parentIds.Where(static id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        if (parentIdSet.Count == 0)
        {
            return [];
        }

        var items = GetAll();
        var childrenByParent = new Dictionary<string, List<TDocument>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var currentParentId = GetParentId(item);
            if (string.IsNullOrWhiteSpace(currentParentId))
            {
                continue;
            }

            if (!childrenByParent.TryGetValue(currentParentId, out var children))
            {
                children = [];
                childrenByParent[currentParentId] = children;
            }

            children.Add(item);
        }

        var descendants = new List<TDocument>();
        var stack = new Stack<string>(parentIdSet);
        var visited = new HashSet<string>(parentIdSet, StringComparer.Ordinal);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                var childId = GetId(child);
                if (!string.IsNullOrWhiteSpace(childId) && visited.Add(childId))
                {
                    descendants.Add(child);
                    stack.Push(childId);
                }
            }
        }

        return descendants;
    }

    private void EnsureParentIndex()
    {
        if (ParentIdProperty.Property is null)
        {
            return;
        }

        Collection.CreateIndex(
            ParentIndexPrefix + CollectionName.ToLowerInvariant(),
            IndexBuilder.ValueIndex(ValueIndexItem.Property(ParentIdProperty.Name)));
    }

    /// <summary>
    /// Writes that bypass <see cref="Save"/> (bulk deletes, imports) still invalidate the snapshot. The
    /// listener is a backstop; bulk paths also invalidate explicitly so the next read sees their result.
    /// </summary>
    private void EnsureChangeListener()
    {
        changeListener?.Remove();
        changeListener = Collection.AddChangeListener((_, _) => InvalidateSnapshot());
    }

    private string? GetId(TDocument document)
    {
        if (IdProperty.Property is null)
        {
            return null;
        }

        return ReadStringProperty(document, IdProperty.Property);
    }

    private string? GetParentId(TDocument document)
    {
        if (ParentIdProperty.Property is null)
        {
            return null;
        }

        return ReadStringProperty(document, ParentIdProperty.Property);
    }

    private static string? ReadStringProperty(TDocument document, System.Reflection.PropertyInfo? property)
    {
        if (property is null || property.PropertyType != typeof(string))
        {
            return null;
        }

        return property.GetValue(document) as string;
    }
}
