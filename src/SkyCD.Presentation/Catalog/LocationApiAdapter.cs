using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Presentation.Locations;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Exposes the open catalog's storage locations through the plugin contract while keeping location storage
/// and inheritance resolution host-internal. Caches the effective projection for one projection pass and
/// invalidates it after every mutation or catalog change.
/// </summary>
public sealed class LocationApiAdapter : ILocationApi, IDisposable
{
    private readonly OpenCatalogState state;
    private CatalogDocumentRepository? boundRepository;
    private CatalogLocationService? service;
    private Dictionary<string, string>? effectiveNames;
    private Dictionary<string, int>? usageCounts;
    private long effectiveNamesTick;
    // One projection pass calls GetEffectiveLocationName for every entry; the TTL keeps that batch on a
    // single catalog scan while still picking up assignments written outside the location API (imports).
    private const int ProjectionCacheMilliseconds = 2000;
    private bool disposed;

    public LocationApiAdapter(OpenCatalogState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        state.Changed += OnCatalogChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<CatalogLocationInfo> GetLocations()
    {
        if (Service() is not { } locations)
        {
            return [];
        }

        var counts = UsageCounts(locations);
        return locations.GetLocations()
            .Select(location => new CatalogLocationInfo(
                location.Id,
                location.Name,
                counts.GetValueOrDefault(location.Id)))
            .ToArray();
    }

    /// <inheritdoc />
    public CatalogLocationInfo CreateLocation(string name)
    {
        var locations = RequireService();
        try
        {
            var created = locations.CreateLocation(name);
            Invalidate();
            return new CatalogLocationInfo(created.Id, created.Name, 0);
        }
        catch (LocationNameRequiredException)
        {
            throw new LocationApiNameRequiredException();
        }
        catch (LocationDuplicateNameException exception)
        {
            throw new LocationApiDuplicateNameException(exception.Name);
        }
    }

    /// <inheritdoc />
    public void RenameLocation(string locationId, string newName)
    {
        var locations = RequireService();
        try
        {
            locations.RenameLocation(locationId, newName);
            Invalidate();
        }
        catch (LocationNameRequiredException)
        {
            throw new LocationApiNameRequiredException();
        }
        catch (LocationDuplicateNameException exception)
        {
            throw new LocationApiDuplicateNameException(exception.Name);
        }
        catch (LocationNotFoundException exception)
        {
            throw new LocationApiNotFoundException(exception.LocationId);
        }
    }

    /// <inheritdoc />
    public void MergeLocations(string sourceLocationId, string targetLocationId)
    {
        var locations = RequireService();
        try
        {
            locations.MergeLocations(sourceLocationId, targetLocationId);
            Invalidate();
        }
        catch (LocationNotFoundException exception)
        {
            throw new LocationApiNotFoundException(exception.LocationId);
        }
    }

    /// <inheritdoc />
    public void DeleteLocation(string locationId)
    {
        var locations = RequireService();
        try
        {
            locations.DeleteLocation(locationId);
            Invalidate();
        }
        catch (LocationNotFoundException exception)
        {
            throw new LocationApiNotFoundException(exception.LocationId);
        }
    }

    /// <inheritdoc />
    public string? GetEffectiveLocationName(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (Service() is not { } locations)
        {
            return null;
        }

        return EffectiveNames(locations).TryGetValue(entryId, out var name) && name.Length > 0 ? name : null;
    }

    /// <inheritdoc />
    public void SetEntryLocation(string entryId, string? locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var locations = RequireService();
        if (state.Repository?.Get(entryId) is null)
        {
            throw new LocationApiEntryNotFoundException(entryId);
        }

        try
        {
            locations.SetEntryLocation(entryId, locationId);
            Invalidate();
        }
        catch (LocationNotFoundException exception)
        {
            throw new LocationApiNotFoundException(exception.LocationId);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        state.Changed -= OnCatalogChanged;
    }

    private void OnCatalogChanged(object? sender, CatalogLifecycle lifecycle)
    {
        boundRepository = null;
        service = null;
        effectiveNames = null;
        usageCounts = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private CatalogLocationService? Service()
    {
        var repository = state.Repository;
        if (repository is null)
        {
            boundRepository = null;
            service = null;
            return null;
        }

        if (ReferenceEquals(repository, boundRepository))
        {
            return service;
        }

        boundRepository = repository;
        effectiveNames = null;
        usageCounts = null;
        service = CreateService(repository);
        return service;
    }

    private CatalogLocationService RequireService() =>
        Service() ?? throw new LocationApiCatalogUnavailableException();

    private Dictionary<string, int> UsageCounts(CatalogLocationService locations)
    {
        if (usageCounts is not null && Environment.TickCount64 - effectiveNamesTick < ProjectionCacheMilliseconds)
        {
            return usageCounts;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var locationId in locations.GetEffectiveLocationIds().Values)
        {
            counts[locationId] = counts.GetValueOrDefault(locationId) + 1;
        }

        usageCounts = counts;
        effectiveNamesTick = Environment.TickCount64;
        return counts;
    }

    private Dictionary<string, string> EffectiveNames(CatalogLocationService locations)
    {
        if (effectiveNames is not null &&
            Environment.TickCount64 - effectiveNamesTick < ProjectionCacheMilliseconds)
        {
            return effectiveNames;
        }

        var names = locations.GetLocations()
            .ToDictionary(static location => location.Id, static location => location.Name, StringComparer.Ordinal);
        effectiveNames = locations.GetEffectiveLocationIds()
            .ToDictionary(
                static pair => pair.Key,
                pair => names.GetValueOrDefault(pair.Value, string.Empty),
                StringComparer.Ordinal);
        effectiveNamesTick = Environment.TickCount64;
        return effectiveNames;
    }

    private void Invalidate()
    {
        effectiveNames = null;
        usageCounts = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static CatalogLocationService? CreateService(CatalogDocumentRepository catalog)
    {
        var database = catalog.Collection?.Database;
        var mapping = (CouchbaseDocument?)typeof(LocationDocument)
            .GetCustomAttributes(typeof(CouchbaseDocument), inherit: false)
            .FirstOrDefault();
        if (database is null || mapping is null || string.IsNullOrWhiteSpace(mapping.CollectionName))
        {
            return null;
        }

        var collection = database.GetCollection(
                mapping.CollectionName,
                global::Couchbase.Lite.Collection.DefaultScopeName)
            ?? database.CreateCollection(
                mapping.CollectionName,
                global::Couchbase.Lite.Collection.DefaultScopeName);
        var repository = new LocationDocumentRepository();
        repository.Initialize(typeof(LocationDocument), mapping.CollectionName, collection);
        return new CatalogLocationService(catalog, repository);
    }
}
