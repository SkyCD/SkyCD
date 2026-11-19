using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.Locations;

/// <summary>
/// Assigns physical storage locations to catalog entries. Assignments are stored as stable location ids
/// in each entry's <c>LocationId</c> property, so they travel with the native catalog document through
/// repository recreation and restart. Location definitions live in <see cref="LocationDocumentRepository"/>
/// and are referenced by stable ids, so renaming never orphans assignments.
///
/// A location assigned to a media/volume entry is inherited by its descendants; any entry may carry its
/// own override. Effective location resolution walks the parent chain to the nearest assignment.
/// </summary>
public sealed class CatalogLocationService(
    CatalogDocumentRepository catalogRepository,
    LocationDocumentRepository locationRepository)
{
    public const string LocationPropertyKey = "LocationId";

    /// <summary>
    /// Source-specific property keys that carry a physical location name at import time. Canonicalized
    /// per source instead of the bare <c>Location</c> key, which other importers use for filesystem paths.
    /// </summary>
    public static readonly IReadOnlyList<string> ImportSourcePropertyKeys = ["PhysicalLocation", "ZdcStorageLocation"];

    private readonly CatalogDocumentRepository catalogRepository =
        catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
    private readonly LocationDocumentRepository locationRepository =
        locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));

    public IReadOnlyList<LocationDocument> GetLocations() => locationRepository.GetOrdered();

    public LocationDocument CreateLocation(string name) => locationRepository.CreateLocation(name);

    /// <summary>
    /// Returns an existing location with the given name or creates one. Used by importers that map a
    /// source-specific location string into the location model.
    /// </summary>
    public LocationDocument EnsureLocation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new LocationNameRequiredException();
        }

        var trimmed = name.Trim();
        return locationRepository.FindByName(trimmed) ?? locationRepository.CreateLocation(trimmed);
    }

    public void RenameLocation(string locationId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new LocationNameRequiredException();
        }

        var location = locationRepository.Get(locationId) ?? throw new LocationNotFoundException(locationId);
        var trimmed = newName.Trim();
        var duplicate = locationRepository.FindByName(trimmed);
        if (duplicate is not null && !string.Equals(duplicate.Id, locationId, StringComparison.Ordinal))
        {
            throw new LocationDuplicateNameException(trimmed);
        }

        if (string.Equals(location.Name, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        location.Name = trimmed;
        locationRepository.Save(location.Id, location);
    }

    /// <summary>
    /// Merges the source location into the target location: every entry assigned the source is assigned
    /// the target instead (including media roots whose descendants inherit it), then the source location
    /// is deleted. No orphan references remain.
    /// </summary>
    public void MergeLocations(string sourceLocationId, string targetLocationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocationId);
        if (string.Equals(sourceLocationId, targetLocationId, StringComparison.Ordinal))
        {
            return;
        }

        _ = locationRepository.Get(sourceLocationId) ?? throw new LocationNotFoundException(sourceLocationId);
        _ = locationRepository.Get(targetLocationId) ?? throw new LocationNotFoundException(targetLocationId);

        foreach (var entry in catalogRepository.GetAll())
        {
            if (!TryReadDirectLocationId(entry, out var assigned) ||
                !string.Equals(assigned, sourceLocationId, StringComparison.Ordinal))
            {
                continue;
            }

            WriteEntryLocationId(entry.Id, targetLocationId);
        }

        DeleteLocationDocument(sourceLocationId);
    }

    /// <summary>
    /// Deletes a location and removes its assignments from every entry, so no orphan references remain.
    /// Descendants that inherited the deleted location fall back to the next ancestor assignment.
    /// </summary>
    public void DeleteLocation(string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        _ = locationRepository.Get(locationId) ?? throw new LocationNotFoundException(locationId);

        foreach (var entry in catalogRepository.GetAll())
        {
            if (!TryReadDirectLocationId(entry, out var assigned) ||
                !string.Equals(assigned, locationId, StringComparison.Ordinal))
            {
                continue;
            }

            ClearEntryLocationId(entry.Id);
        }

        DeleteLocationDocument(locationId);
    }

    /// <summary>
    /// Returns the number of catalog entries whose effective location (own assignment or inherited from
    /// an ancestor) is the given location. Used for in-use reporting before rename, merge and delete.
    /// </summary>
    public int CountUsages(string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        return GetEffectiveLocationIds().Values.Count(id =>
            string.Equals(id, locationId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the number of entries carrying a direct (non-inherited) assignment to the location.
    /// </summary>
    public int CountDirectUsages(string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        return catalogRepository.GetAll().Count(entry =>
            TryReadDirectLocationId(entry, out var assigned) &&
            string.Equals(assigned, locationId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns every entry whose effective location is the given location, including descendants that
    /// inherit it from an ancestor.
    /// </summary>
    public IReadOnlyList<CatalogDocument> GetEntriesWithLocation(string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        var effective = GetEffectiveLocationIds();
        return catalogRepository.GetAll()
            .Where(entry => effective.TryGetValue(entry.Id, out var id) &&
                            string.Equals(id, locationId, StringComparison.Ordinal))
            .OrderBy(static entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns the location id directly assigned to the entry, ignoring inheritance. Empty when the
    /// entry only inherits its location from an ancestor.
    /// </summary>
    public string? GetEntryOwnLocationId(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var entry = catalogRepository.Get(entryId);
        return entry is not null && TryReadDirectLocationId(entry, out var assigned) ? assigned : null;
    }

    /// <summary>
    /// Returns the effective location id for the entry: its own assignment, or the nearest ancestor
    /// assignment when it has none. Null when no ancestor chain member carries one.
    /// </summary>
    public string? GetEntryEffectiveLocationId(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var entry = catalogRepository.Get(entryId);
        return entry is null ? null : ResolveEffectiveLocationId(entry);
    }

    /// <summary>
    /// Sets or clears the entry's own location override. Passing null clears the override so the entry
    /// inherits its location from the nearest ancestor again.
    /// </summary>
    public void SetEntryLocation(string entryId, string? locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        _ = catalogRepository.Get(entryId)
            ?? throw new InvalidOperationException($"Catalog entry '{entryId}' does not exist.");

        if (string.IsNullOrWhiteSpace(locationId))
        {
            ClearEntryLocationId(entryId);
            return;
        }

        _ = locationRepository.Get(locationId) ?? throw new LocationNotFoundException(locationId);
        WriteEntryLocationId(entryId, locationId);
    }

    /// <summary>
    /// Resolves the effective location id for an entry without loading the whole catalog again.
    /// </summary>
    public string? ResolveEffectiveLocationId(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ResolveEffectiveLocationId(entry, BuildLookup());
    }

    /// <summary>
    /// Projects the resolved location name onto every entry, mirroring the lending projection. Entries
    /// with no effective location keep an empty <see cref="CatalogDocument.Location"/>.
    /// </summary>
    public IReadOnlyList<CatalogDocument> ProjectLocations(IReadOnlyList<CatalogDocument> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var lookup = BuildLookup(entries);
        var names = BuildNameLookup();
        foreach (var entry in entries)
        {
            var locationId = ResolveEffectiveLocationId(entry, lookup);
            entry.SetLocationName(
                locationId is not null && names.TryGetValue(locationId, out var name) ? name : null);
        }

        return entries;
    }

    /// <summary>
    /// Returns the effective location name for the entry, or an empty string when none is assigned.
    /// </summary>
    public string GetEffectiveLocationName(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var lookup = BuildLookup();
        var names = BuildNameLookup();
        var locationId = ResolveEffectiveLocationId(entry, lookup);
        return locationId is not null && names.TryGetValue(locationId, out var name) ? name : string.Empty;
    }

    /// <summary>
    /// Strips assignments that reference deleted locations. Returns the number of stripped references.
    /// </summary>
    public int RemoveMissingLocationReferences()
    {
        var known = new HashSet<string>(
            locationRepository.GetAll().Select(static location => location.Id),
            StringComparer.Ordinal);
        var stripped = 0;
        foreach (var entry in catalogRepository.GetAll())
        {
            if (!TryReadDirectLocationId(entry, out var assigned))
            {
                continue;
            }

            if (known.Contains(assigned))
            {
                continue;
            }

            stripped++;
            ClearEntryLocationId(entry.Id);
        }

        return stripped;
    }

    /// <summary>
    /// Canonicalizes source-specific location properties (<c>PhysicalLocation</c>, <c>ZdcStorageLocation</c>)
    /// into the location model on the given entry. The source key is preserved in properties so exports
    /// that round-trip it stay lossless. Returns the created or matched location name, or null when the
    /// entry carried no mappable location data.
    /// </summary>
    public string? CanonicalizeImportedLocation(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var key in ImportSourcePropertyKeys)
        {
            if (!entry.Properties.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            var name = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var location = EnsureLocation(name!);
            WriteEntryLocationId(entry.Id, location.Id);
            return location.Name;
        }

        return null;
    }

    /// <summary>
    /// Migrates existing catalogs to location storage without changing the source hierarchy: entries
    /// carrying a source-specific location property are canonicalized when they have no assignment yet.
    /// Returns the number of entries normalized.
    /// </summary>
    public int NormalizeAllEntries()
    {
        var normalized = 0;
        foreach (var entry in catalogRepository.GetAll())
        {
            if (TryReadDirectLocationId(entry, out _))
            {
                continue;
            }

            if (CanonicalizeImportedLocation(entry) is not null)
            {
                normalized++;
            }
        }

        return normalized;
    }

    private Dictionary<string, string> GetEffectiveLocationIds()
    {
        var lookup = BuildLookup();
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in lookup.Values)
        {
            var locationId = ResolveEffectiveLocationId(entry, lookup);
            if (locationId is not null)
            {
                effective[entry.Id] = locationId;
            }
        }

        return effective;
    }

    private static string? ResolveEffectiveLocationId(
        CatalogDocument entry,
        IReadOnlyDictionary<string, CatalogDocument> lookup)
    {
        var current = entry;
        var guard = 0;
        while (current is not null && guard++ <= 10_000)
        {
            if (TryReadDirectLocationId(current, out var assigned))
            {
                return assigned;
            }

            if (string.IsNullOrEmpty(current.ParentId) ||
                !lookup.TryGetValue(current.ParentId!, out current))
            {
                return null;
            }
        }

        return null;
    }

    private Dictionary<string, CatalogDocument> BuildLookup()
        => BuildLookup(catalogRepository.GetAll());

    private static Dictionary<string, CatalogDocument> BuildLookup(IReadOnlyList<CatalogDocument> entries)
    {
        var lookup = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            lookup[entry.Id] = entry;
        }

        return lookup;
    }

    private Dictionary<string, string> BuildNameLookup()
    {
        return locationRepository.GetAll()
            .Where(static location => !string.IsNullOrWhiteSpace(location.Id))
            .ToDictionary(static location => location.Id, static location => location.Name, StringComparer.Ordinal);
    }

    private static bool TryReadDirectLocationId(CatalogDocument entry, out string locationId)
    {
        locationId = string.Empty;
        if (!entry.Properties.TryGetValue(LocationPropertyKey, out var raw) || raw is null)
        {
            return false;
        }

        var text = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        locationId = text!;
        return true;
    }

    private void WriteEntryLocationId(string entryId, string locationId)
    {
        var entry = catalogRepository.Get(entryId);
        if (entry is null)
        {
            return;
        }

        var properties = new PropertiesCollection(entry.Properties)
        {
            [LocationPropertyKey] = locationId
        };
        SaveWithProperties(entry, properties);
    }

    private void ClearEntryLocationId(string entryId)
    {
        var entry = catalogRepository.Get(entryId);
        if (entry is null || !entry.Properties.ContainsKey(LocationPropertyKey))
        {
            return;
        }

        var properties = new PropertiesCollection(entry.Properties);
        properties.Remove(LocationPropertyKey);
        SaveWithProperties(entry, properties);
    }

    private void SaveWithProperties(CatalogDocument entry, PropertiesCollection properties)
    {
        catalogRepository.Save(entry.Id, new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            ParentId = entry.ParentId,
            Type = entry.Type,
            Size = entry.Size,
            ChildrenCount = entry.ChildrenCount,
            ContentsState = entry.ContentsState,
            Status = entry.Status,
            Properties = properties
        });
    }

    private void DeleteLocationDocument(string locationId)
    {
        using var document = locationRepository.Collection.GetDocument(locationId);
        if (document is not null)
        {
            locationRepository.Collection.Delete(document);
        }
    }
}
