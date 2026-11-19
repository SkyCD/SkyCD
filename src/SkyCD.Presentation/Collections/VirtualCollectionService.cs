using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Search;

namespace SkyCD.Presentation.Collections;

/// <summary>
/// Static virtual collections (named sets of entry ids) and dynamic
/// collections (saved advanced-search queries evaluated on demand).
/// Membership references entries; removing a membership or a collection
/// never deletes catalog entries.
/// </summary>
public sealed class VirtualCollectionService
{
    private static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly CatalogDocumentRepository catalogRepository;
    private readonly VirtualCollectionDocumentRepository collectionRepository;
    private readonly Func<AdvancedCatalogSearchService.MetadataLookup?>? metadataLookupFactory;
    private readonly Func<IReadOnlyList<CatalogDocument>, IReadOnlyList<CatalogDocument>>? projectEntries;

    public VirtualCollectionService(
        CatalogDocumentRepository catalogRepository,
        VirtualCollectionDocumentRepository collectionRepository,
        Func<AdvancedCatalogSearchService.MetadataLookup?>? metadataLookupFactory = null,
        Func<IReadOnlyList<CatalogDocument>, IReadOnlyList<CatalogDocument>>? projectEntries = null)
    {
        this.catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
        this.collectionRepository = collectionRepository ?? throw new ArgumentNullException(nameof(collectionRepository));
        this.metadataLookupFactory = metadataLookupFactory;
        this.projectEntries = projectEntries;
    }

    public IReadOnlyList<VirtualCollectionDocument> GetCollections() =>
        collectionRepository.GetOrdered();

    public VirtualCollectionDocument GetRequired(string collectionId)
    {
        var collection = string.IsNullOrWhiteSpace(collectionId)
            ? null
            : collectionRepository.Get(collectionId);
        return collection ?? throw new CollectionNotFoundException(collectionId);
    }

    public VirtualCollectionDocument CreateCollection(string name) =>
        collectionRepository.CreateCollection(name);

    public void RenameCollection(string collectionId, string newName)
    {
        var collection = GetRequired(collectionId);
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new CollectionNameRequiredException();
        }

        var trimmed = newName.Trim();
        var duplicate = collectionRepository.FindByName(trimmed);
        if (duplicate is not null && !string.Equals(duplicate.Id, collectionId, StringComparison.Ordinal))
        {
            throw new CollectionDuplicateNameException(trimmed);
        }

        collection.Name = trimmed;
        collectionRepository.Save(collection.Id, collection);
    }

    public void DeleteCollection(string collectionId)
    {
        var collection = GetRequired(collectionId);
        using var stored = collectionRepository.Collection.GetDocument(collection.Id);
        if (stored is not null)
        {
            collectionRepository.Collection.Delete(stored);
        }
    }

    public IReadOnlyList<string> GetEntryIds(string collectionId) =>
        NormalizeIds(GetRequired(collectionId).EntryIds);

    public void SetEntryIds(string collectionId, IEnumerable<string> entryIds)
    {
        var collection = GetRequired(collectionId);
        EnsureStatic(collection);
        collection.EntryIds = NormalizeIds(entryIds).ToList();
        collectionRepository.Save(collection.Id, collection);
    }

    public void AddEntries(string collectionId, IEnumerable<string> entryIds)
    {
        var collection = GetRequired(collectionId);
        EnsureStatic(collection);
        var merged = new HashSet<string>(collection.EntryIds ?? [], StringComparer.Ordinal);
        foreach (var id in entryIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                merged.Add(id.Trim());
            }
        }

        collection.EntryIds = merged.OrderBy(static id => id, StringComparer.Ordinal).ToList();
        collectionRepository.Save(collection.Id, collection);
    }

    public void RemoveEntries(string collectionId, IEnumerable<string> entryIds)
    {
        var collection = GetRequired(collectionId);
        EnsureStatic(collection);
        var removed = new HashSet<string>(
            (entryIds ?? []).Where(static id => !string.IsNullOrWhiteSpace(id)).Select(static id => id.Trim()),
            StringComparer.Ordinal);
        collection.EntryIds = (collection.EntryIds ?? [])
            .Where(id => !removed.Contains(id))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();
        collectionRepository.Save(collection.Id, collection);
    }

    public IReadOnlyList<VirtualCollectionDocument> FindCollectionsContaining(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return [];
        }

        return collectionRepository.GetAll()
            .Where(collection => collection.Kind == VirtualCollectionKind.Static
                && (collection.EntryIds ?? []).Contains(entryId, StringComparer.Ordinal))
            .OrderBy(static collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Resolves a collection to its current entries. Missing entries are
    /// skipped deterministically and reported through <paramref name="missingCount"/>.
    /// </summary>
    public IReadOnlyList<CatalogDocument> ResolveEntries(string collectionId, out int missingCount)
    {
        var collection = GetRequired(collectionId);
        if (collection.Kind == VirtualCollectionKind.Dynamic)
        {
            missingCount = 0;
            return EvaluateDynamic(collection);
        }

        var resolved = new List<CatalogDocument>();
        missingCount = 0;
        foreach (var id in NormalizeIds(collection.EntryIds))
        {
            var entry = catalogRepository.Get(id);
            if (entry is null)
            {
                missingCount++;
                continue;
            }

            resolved.Add(entry);
        }

        return resolved;
    }

    /// <summary>
    /// Drops references to deleted entries and persists the cleaned list.
    /// Returns the number of removed references.
    /// </summary>
    public int PruneDeletedReferences(string collectionId)
    {
        var collection = GetRequired(collectionId);
        EnsureStatic(collection);
        var kept = (collection.EntryIds ?? []).Where(id => catalogRepository.Get(id) is not null).ToList();
        var removed = (collection.EntryIds ?? []).Count - kept.Count;
        if (removed > 0)
        {
            collection.EntryIds = kept;
            collectionRepository.Save(collection.Id, collection);
        }

        return removed;
    }

    public VirtualCollectionDocument CreateDynamicCollection(string name, AdvancedCatalogQuery query)
    {
        var json = SerializeQuery(query);
        var collection = collectionRepository.CreateCollection(name);
        collection.Kind = VirtualCollectionKind.Dynamic;
        collection.SavedQueryJson = json;
        collection.EntryIds = [];
        collectionRepository.Save(collection.Id, collection);
        return collection;
    }

    public void UpdateDynamicCollection(string collectionId, AdvancedCatalogQuery query)
    {
        var collection = GetRequired(collectionId);
        if (collection.Kind != VirtualCollectionKind.Dynamic)
        {
            throw new SavedSearchException();
        }

        collection.SavedQueryJson = SerializeQuery(query);
        collectionRepository.Save(collection.Id, collection);
    }

    public AdvancedCatalogQuery GetDynamicQuery(string collectionId)
    {
        var collection = GetRequired(collectionId);
        if (collection.Kind != VirtualCollectionKind.Dynamic || string.IsNullOrWhiteSpace(collection.SavedQueryJson))
        {
            throw new InvalidOperationException($"Collection '{collectionId}' has no saved search.");
        }

        return DeserializeQuery(collection.SavedQueryJson);
    }

    private static void EnsureStatic(VirtualCollectionDocument collection)
    {
        if (collection.Kind != VirtualCollectionKind.Static)
        {
            throw new InvalidOperationException($"Collection '{collection.Name}' is dynamic; membership is evaluated, not stored.");
        }
    }

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string>? ids) =>
        (ids ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

    private IReadOnlyList<CatalogDocument> EvaluateDynamic(VirtualCollectionDocument collection)
    {
        if (string.IsNullOrWhiteSpace(collection.SavedQueryJson))
        {
            throw new SavedSearchException();
        }

        var query = DeserializeQuery(collection.SavedQueryJson);
        var index = new CatalogSearchIndex();
        var entries = catalogRepository.GetAll();
        index.Rebuild(projectEntries?.Invoke(entries) ?? entries);
        var matches = AdvancedCatalogSearchService.Search(index, query, metadataLookupFactory?.Invoke());
        var resolved = new List<CatalogDocument>(matches.Count);
        foreach (var match in matches)
        {
            resolved.Add(match.Document);
        }

        return resolved;
    }

    public static string SerializeQuery(AdvancedCatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        AdvancedCatalogSearchService.Validate(query);

        return JsonSerializer.Serialize(query, QueryJsonOptions);
    }

    public static AdvancedCatalogQuery DeserializeQuery(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("version", out _)
                || !document.RootElement.TryGetProperty("root", out _)
                || !document.RootElement.TryGetProperty("scope", out _))
            {
                throw new SavedSearchException();
            }

            var query = JsonSerializer.Deserialize<AdvancedCatalogQuery>(json, QueryJsonOptions)
                ?? throw new SavedSearchException();
            query = NormalizeQueryValues(query);
            AdvancedCatalogSearchService.Validate(query);
            return query;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new SavedSearchException();
        }
    }

    private static AdvancedCatalogQuery NormalizeQueryValues(AdvancedCatalogQuery query) =>
        query with
        {
            Root = NormalizeGroup(query.Root),
            Take = query.Take
        };

    private static CatalogQueryGroup NormalizeGroup(CatalogQueryGroup group) =>
        group with
        {
            Predicates = group.Predicates.Select(static predicate => predicate with
            {
                Value = NormalizeValue(predicate.Value),
                Values = predicate.Values?.Select(NormalizeValue).ToArray()
            }).ToArray(),
            Groups = group.Groups.Select(NormalizeGroup).ToArray()
        };

    private static object? NormalizeValue(object? value) => value switch
    {
        JsonElement element => NormalizeElement(element),
        _ => value
    };

    private static object? NormalizeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => element.EnumerateArray().Select(NormalizeElement).ToArray(),
        _ => element.GetRawText()
    };
}
