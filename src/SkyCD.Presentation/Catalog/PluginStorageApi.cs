using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Plugin.Abstractions.Capabilities.Storage;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Host storage for one plugin identity. Collection names are namespaced per plugin so distinct plugins never
/// collide, and the data lives in the open catalog database next to the core collections.
/// </summary>
public sealed class PluginStorageApi(OpenCatalogState state, string pluginId) : IPluginStorageApi
{
    private const string IdField = "Id";

    /// <inheritdoc />
    public IPluginCollection OpenCollection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var database = state.Repository?.Collection?.Database
                       ?? throw new PluginStorageUnavailableException();
        var collectionName = Namespaced(name);
        var collection = database.GetCollection(collectionName, Collection.DefaultScopeName)
                         ?? database.CreateCollection(collectionName, Collection.DefaultScopeName);
        return new PluginCollection(name, collection);
    }

    private string Namespaced(string name)
    {
        // Collection names accept letters, digits, '-' and '_'; fold everything else to keep the id stable.
        var combined = new string((pluginId + "-" + name)
            .Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        return "plugin-" + combined;
    }

    private sealed class PluginCollection(string name, Collection collection) : IPluginCollection
    {
        public string Name { get; } = name;

        public PluginDocumentRecord? Get(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            using var document = collection.GetDocument(id);
            return document is null ? null : new PluginDocumentRecord(id, ToFields(document.ToDictionary()));
        }

        public IReadOnlyList<PluginDocumentRecord> GetAll()
        {
            using var query = QueryBuilder
                .Select(SelectResult.All())
                .From(DataSource.Collection(collection));
            using var results = query.Execute();
            var records = new List<PluginDocumentRecord>();
            foreach (var row in results)
            {
                var dictionary = row.GetDictionary(collection.Name);
                if (dictionary is null)
                {
                    continue;
                }

                var fields = ToFields(dictionary.ToDictionary());
                if (!fields.TryGetValue(IdField, out var id) || string.IsNullOrWhiteSpace(id?.ToString()))
                {
                    continue;
                }

                records.Add(new PluginDocumentRecord(id!.ToString()!, fields));
            }

            return records;
        }

        public void Upsert(string id, IReadOnlyDictionary<string, object?> fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(fields);
            using var document = new MutableDocument(id);
            document.SetString(IdField, id);
            foreach (var (key, value) in fields)
            {
                if (value is not null && !string.Equals(key, IdField, StringComparison.Ordinal))
                {
                    document.SetValue(key, value);
                }
            }

            collection.Save(document);
        }

        public void Delete(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            using var document = collection.GetDocument(id);
            if (document is not null)
            {
                collection.Delete(document);
            }
        }

        private static IReadOnlyDictionary<string, object?> ToFields(IReadOnlyDictionary<string, object?> source) =>
            source.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }
}
