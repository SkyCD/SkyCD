using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class VirtualCollectionDocumentRepository : RepositoryBase<VirtualCollectionDocument>
{
    public IReadOnlyList<VirtualCollectionDocument> GetOrdered()
    {
        return GetAll()
            .Where(static collection => !string.IsNullOrWhiteSpace(collection.Name))
            .OrderBy(static collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public VirtualCollectionDocument? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        return GetAll().FirstOrDefault(collection =>
            string.Equals(collection.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public VirtualCollectionDocument CreateCollection(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CollectionNameRequiredException();
        }

        var trimmed = name.Trim();
        if (FindByName(trimmed) is not null)
        {
            throw new CollectionDuplicateNameException(trimmed);
        }

        var collection = new VirtualCollectionDocument
        {
            Id = BuildCollectionId(trimmed, GetAll()),
            Name = trimmed
        };
        Save(collection.Id, collection);
        return collection;
    }

    internal static string BuildCollectionId(string name, IEnumerable<VirtualCollectionDocument> existing)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray()).Trim('-');
        if (slug.Length == 0)
        {
            slug = "collection";
        }

        var candidate = slug;
        var counter = 2;
        var taken = new HashSet<string>(existing.Select(static collection => collection.Id), StringComparer.Ordinal);
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{counter}";
            counter++;
        }

        return candidate;
    }
}
