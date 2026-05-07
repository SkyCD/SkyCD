using System;
using System.Collections.Generic;
using Couchbase.Lite;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Helpers;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Repository;

/// <summary>
/// Tree-aware repository helpers for documents that expose string id properties configured at repository creation.
/// </summary>
public class TreeRepository : RepositoryBase
{
    public DocumentPropertyBinding ParentIdProperty { get; internal set; } = new("ParentId", null);

    internal override void Initialize(Type documentType, string collectionName, Collection collection)
    {
        base.Initialize(documentType, collectionName, collection);
        
        ParentIdProperty = AttributeHelper.ResolveStringPropertyWithAttributeOrDefault(
            documentType: documentType,
            attributeType: typeof(ParentId),
            defaultPropertyName: "ParentId");
    }

    public IReadOnlyList<TDocument> GetRoots<TDocument>()
        where TDocument : class, new()
    {
        var roots = new List<TDocument>();
        foreach (var item in GetAll<TDocument>())
        {
            if (string.IsNullOrWhiteSpace(GetParentId(item)))
            {
                roots.Add(item);
            }
        }

        return roots;
    }

    public IReadOnlyList<TDocument> GetChildrenOf<TDocument>(string parentId)
        where TDocument : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);

        var children = new List<TDocument>();
        foreach (var item in GetAll<TDocument>())
        {
            if (string.Equals(GetParentId(item), parentId, StringComparison.Ordinal))
            {
                children.Add(item);
            }
        }

        return children;
    }

    public IReadOnlyList<TDocument> GetDescendantsOf<TDocument>(string parentId)
        where TDocument : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);

        var items = GetAll<TDocument>();
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
        var stack = new Stack<string>();
        stack.Push(parentId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                descendants.Add(child);
                var childId = GetId(child);
                if (!string.IsNullOrWhiteSpace(childId))
                {
                    stack.Push(childId);
                }
            }
        }

        return descendants;
    }

    private string? GetId<TDocument>(TDocument document)
        where TDocument : class
    {
        if (IdProperty.Property is null)
        {
            return null;
        }

        return ReadStringProperty(document, IdProperty.Property);
    }

    private string? GetParentId<TDocument>(TDocument document)
        where TDocument : class
    {
        if (ParentIdProperty.Property is null)
        {
            return null;
        }

        return ReadStringProperty(document, ParentIdProperty.Property);
    }

    private static string? ReadStringProperty<TDocument>(TDocument document, System.Reflection.PropertyInfo? property)
        where TDocument : class
    {
        if (property is null || property.PropertyType != typeof(string))
        {
            return null;
        }

        return property.GetValue(document) as string;
    }
}
