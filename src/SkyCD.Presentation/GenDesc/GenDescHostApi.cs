using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Formatting;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.SDK.Capabilities.GenDesc;

namespace SkyCD.Presentation.GenDesc;

/// <summary>
/// Resolves catalog entries into GenDesc facts. The plugin never touches the catalog or the media cache
/// directly: entry eligibility, the local-source rule, MIME resolution and metadata merging live here.
/// </summary>
public sealed class GenDescHostApi(OpenCatalogState catalogState) : IGenDescHostApi
{
    private const string SourcePathKey = "FullName";

    /// <inheritdoc />
    public Task<GenDescEntryContext?> GetEntryContextAsync(string entryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        var repository = catalogState.Repository;
        if (repository is null)
        {
            return Task.FromResult<GenDescEntryContext?>(null);
        }

        var entry = repository.Get(entryId);
        if (entry is null || entry.Type != CatalogDocumentType.File)
        {
            return Task.FromResult<GenDescEntryContext?>(null);
        }

        var catalog = repository.GetAll();
        if (!IsLocalFile(entry, catalog))
        {
            return Task.FromResult<GenDescEntryContext?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddBaseFacts(entry, facts);
        foreach (var property in entry.Properties)
        {
            AddFact(facts, property.Key, property.Value?.ToString());
        }

        AddMediaFacts(entry, catalog, repository.Collection?.Database, facts);

        var extension = Path.GetExtension(entry.Name);
        return Task.FromResult<GenDescEntryContext?>(new GenDescEntryContext(
            entry.Name,
            extension.Length == 0 ? string.Empty : extension.ToLowerInvariant(),
            FileMimeTypeResolver.Resolve(entry) ?? string.Empty,
            facts));
    }

    private static void AddBaseFacts(CatalogDocument entry, Dictionary<string, string> facts)
    {
        var baseName = Path.GetFileNameWithoutExtension(entry.Name);
        if (baseName.Length == 0)
        {
            baseName = entry.Name;
        }

        AddFact(facts, "Name", baseName);
        AddFact(facts, "Title", baseName);
        AddFact(facts, "FileName", entry.Name);
        AddFact(facts, "File", entry.Name);
        var extension = Path.GetExtension(entry.Name);
        AddFact(facts, "Extension", extension.Length == 0 ? string.Empty : extension.ToLowerInvariant());
        AddFact(facts, "MimeType", FileMimeTypeResolver.Resolve(entry));
        AddFact(facts, "SizeBytes", entry.Size.ToString(CultureInfo.InvariantCulture));
        AddFact(facts, "Size", SizeFormatting.FormatInvariantBytes(Math.Max(0, entry.Size)));
        if (entry.Properties.TryGetValue(SourcePathKey, out var path) && path?.ToString() is { Length: > 0 } fullName)
        {
            AddFact(facts, "Path", fullName);
            AddFact(facts, "Directory", Path.GetDirectoryName(fullName));
        }
    }

    private static void AddMediaFacts(CatalogDocument entry, IReadOnlyList<CatalogDocument> catalog,
        Database? database, Dictionary<string, string> facts)
    {
        if (database is null)
        {
            return;
        }

        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, catalog);
        using var store = MediaCacheStore.Create(database);
        foreach (var record in store.GetRecordsForEntry(entry.Id)
                     .Where(record => record.State == MediaCacheRecordState.Captured &&
                                      string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
                     .OrderByDescending(record => record.Priority)
                     .ThenBy(record => record.Id, StringComparer.Ordinal))
        {
            foreach (var field in record.Metadata.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                var text = FormatPayload(field.GetPayload());
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                AddFact(facts, field.Key, text);
                AddFact(facts, "Metadata." + field.Key, text);
            }
        }
    }

    private static bool IsLocalFile(CatalogDocument entry, IReadOnlyList<CatalogDocument> catalog)
    {
        if (!entry.Properties.TryGetValue(SourcePathKey, out var path) || string.IsNullOrWhiteSpace(path?.ToString()))
        {
            return false;
        }

        var byId = catalog
            .GroupBy(document => document.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Id };
        for (var parentId = entry.ParentId;
             !string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId) && byId.TryGetValue(parentId, out var parent);
             parentId = parent.ParentId)
        {
            if (parent.Type == CatalogDocumentType.NetworkFolder)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddFact(Dictionary<string, string> facts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || facts.ContainsKey(key))
        {
            return;
        }

        facts[key] = trimmed.Length > 4000 ? trimmed[..4000] : trimmed;
    }

    private static string? FormatPayload(object? payload) => payload switch
    {
        null => null,
        string text => text,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => payload.ToString()
    };

}
