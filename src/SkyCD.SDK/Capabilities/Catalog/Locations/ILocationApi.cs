using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>
/// Host surface for reading and editing the open catalog's storage locations. Location assignments are
/// inherited from ancestors, so the effective location reported for an entry may come from its parent.
/// </summary>
public interface ILocationApi
{
    /// <summary>Raised after a location definition, assignment, or the open catalog changes.</summary>
    event EventHandler? Changed;

    /// <summary>Gets whether a catalog is open; mutations throw <c>LocationApiCatalogUnavailableException</c> without one.</summary>
    bool IsCatalogOpen { get; }

    /// <summary>Gets every location with its effective usage count, ordered by name; empty when no catalog is open.</summary>
    IReadOnlyList<CatalogLocationInfo> GetLocations();

    /// <summary>Creates a location and returns it with a zero usage count.</summary>
    CatalogLocationInfo CreateLocation(string name);

    /// <summary>Renames a location; assignments are untouched because they reference the stable id.</summary>
    void RenameLocation(string locationId, string newName);

    /// <summary>Reassigns entries assigned to the source location to the target, then deletes the source.</summary>
    void MergeLocations(string sourceLocationId, string targetLocationId);

    /// <summary>Deletes a location and clears its assignments so no orphan references remain.</summary>
    void DeleteLocation(string locationId);

    /// <summary>Gets the effective location name for an entry, or null when none is assigned.</summary>
    string? GetEffectiveLocationName(string entryId);

    /// <summary>Sets or clears the entry's own location override; descendants keep inheriting it.</summary>
    void SetEntryLocation(string entryId, string? locationId);
}
