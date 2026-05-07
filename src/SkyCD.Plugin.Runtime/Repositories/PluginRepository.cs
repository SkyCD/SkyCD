using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Repositories;

public sealed class PluginRepository : RepositoryBase
{
    public IReadOnlyList<PluginDocument> GetAll()
    {
        return GetAll<PluginDocument>()
            .Where(static mapped => !string.IsNullOrWhiteSpace(mapped.Id))
            .Select(static mapped =>
            {
                mapped.Constraints ??= new PluginConstraintsDocument();
                return mapped;
            })
            .ToList();
    }

    public void UpsertPluginDocuments(IReadOnlyCollection<PluginDocument> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        var existingById = GetAll<PluginDocument>()
            .ToDictionary(static descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);

        var discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in discovered)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                continue;
            }

            discoveredIds.Add(document.Id);
            if (existingById.TryGetValue(document.Id, out var existing))
            {
                document.IsEnabled = existing.IsEnabled;
            }

            Save(document.Id, document);
            existingById[document.Id] = document;
        }

        foreach (var existing in existingById.Values.Where(existing => !discoveredIds.Contains(existing.Id)))
        {
            existing.IsAvailable = false;
            Save(existing.Id, existing);
        }
    }
}
