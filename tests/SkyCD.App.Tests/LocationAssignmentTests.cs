using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Staging;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LocationAssignmentTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-locations-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public LocationAssignmentTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class Stack : IDisposable
    {
        public Stack()
        {
            Container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Container);
            var repositories = Container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
            Locations = (LocationDocumentRepository)repositories.For<LocationDocument>();
            Service = new CatalogLocationService(Catalog, Locations);
        }

        public Container Container { get; }

        public CatalogDocumentRepository Catalog { get; }

        public LocationDocumentRepository Locations { get; }

        public CatalogLocationService Service { get; }

        public void Dispose() => Container.Dispose();
    }

    private static Stack CreateStack() => new();

    private static CatalogDocument Entry(string id, string? parentId = null, CatalogDocumentType type = CatalogDocumentType.File)
    {
        return new CatalogDocument
        {
            Id = id,
            Name = id,
            ParentId = parentId,
            Type = type,
            Size = 100,
            ChildrenCount = 0,
            Properties = new PropertiesCollection()
        };
    }

    [Fact]
    public void Create_RenameAndDuplicateNames_AreValidated()
    {
        using var stack = CreateStack();
        var garage = stack.Service.CreateLocation("Garage");
        Assert.Equal("loc-garage", garage.Id);

        Assert.Throws<LocationDuplicateNameException>(() => stack.Service.CreateLocation("garage"));
        Assert.Throws<LocationDuplicateNameException>(() => stack.Service.CreateLocation("  GARAGE  "));
        Assert.Throws<LocationNameRequiredException>(() => stack.Service.CreateLocation("   "));

        stack.Service.RenameLocation(garage.Id, "Shelf A");
        Assert.Equal("Shelf A", stack.Service.GetLocations().Single().Name);
        Assert.Throws<LocationDuplicateNameException>(() =>
        {
            var other = stack.Service.CreateLocation("Other");
            stack.Service.RenameLocation(other.Id, "shelf a");
        });
    }

    [Fact]
    public void Assignments_SurviveRepositoryRecreation()
    {
        using (var stack = CreateStack())
        {
            stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
            stack.Catalog.Save("file", Entry("file", "media"));
            var garage = stack.Service.CreateLocation("Garage");
            stack.Service.SetEntryLocation("media", garage.Id);
            stack.Service.SetEntryLocation("file", garage.Id);
        }

        using var reopened = CreateStack();
        var service = reopened.Service;
        Assert.Equal("Garage", service.GetEntryOwnLocationId("media"));
        Assert.Equal("Garage", service.GetEntryEffectiveLocationId("file"));
        Assert.Equal("Garage", service.GetLocations().Single().Name);
    }

    [Fact]
    public void Inheritance_MediaRootLocationReachesDescendants()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("folder", Entry("folder", "media", CatalogDocumentType.Folder));
        stack.Catalog.Save("file", Entry("file", "folder"));
        var garage = stack.Service.CreateLocation("Garage");
        stack.Service.SetEntryLocation("media", garage.Id);

        Assert.Equal(garage.Id, stack.Service.GetEntryEffectiveLocationId("file"));
        Assert.Null(stack.Service.GetEntryOwnLocationId("file"));
        Assert.Equal("Garage", stack.Service.GetEffectiveLocationName(stack.Catalog.Get("file")!));
    }

    [Fact]
    public void Inheritance_EntryOverrideWinsOverInheritedAndClearingFallsBack()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("file", Entry("file", "media"));
        var garage = stack.Service.CreateLocation("Garage");
        var shelf = stack.Service.CreateLocation("Shelf");
        stack.Service.SetEntryLocation("media", garage.Id);

        stack.Service.SetEntryLocation("file", shelf.Id);
        Assert.Equal(shelf.Id, stack.Service.GetEntryEffectiveLocationId("file"));

        stack.Service.SetEntryLocation("file", null);
        Assert.Null(stack.Service.GetEntryOwnLocationId("file"));
        Assert.Equal(garage.Id, stack.Service.GetEntryEffectiveLocationId("file"));
    }

    [Fact]
    public void CountUsages_CountsInheritedUsage()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("folder", Entry("folder", "media", CatalogDocumentType.Folder));
        stack.Catalog.Save("file", Entry("file", "folder"));
        stack.Catalog.Save("other", Entry("other"));
        var garage = stack.Service.CreateLocation("Garage");
        var shelf = stack.Service.CreateLocation("Shelf");
        stack.Service.SetEntryLocation("media", garage.Id);
        stack.Service.SetEntryLocation("other", shelf.Id);

        Assert.Equal(3, stack.Service.CountUsages(garage.Id));
        Assert.Equal(1, stack.Service.CountDirectUsages(garage.Id));
        Assert.Equal(1, stack.Service.CountUsages(shelf.Id));
        Assert.Equal(3, stack.Service.GetEntriesWithLocation(garage.Id).Count);
    }

    [Fact]
    public void Merge_MovesAssignmentsDeletesSourceAndLeavesNoOrphans()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("file", Entry("file", "media"));
        stack.Catalog.Save("b", Entry("b"));
        var source = stack.Service.CreateLocation("Old");
        var target = stack.Service.CreateLocation("New");
        stack.Service.SetEntryLocation("media", source.Id);
        stack.Service.SetEntryLocation("b", source.Id);

        stack.Service.MergeLocations(source.Id, target.Id);

        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("media"));
        Assert.Equal(target.Id, stack.Service.GetEntryEffectiveLocationId("file"));
        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("b"));
        Assert.DoesNotContain(stack.Service.GetLocations(), location => location.Id == source.Id);
        Assert.Throws<LocationNotFoundException>(() => stack.Service.MergeLocations(source.Id, target.Id));
    }

    [Fact]
    public void Delete_SweepsAssignmentsAndDescendantsFallBack()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("file", Entry("file", "media"));
        stack.Catalog.Save("b", Entry("b"));
        var doomed = stack.Service.CreateLocation("Temp");
        var kept = stack.Service.CreateLocation("Kept");
        stack.Service.SetEntryLocation("media", doomed.Id);
        stack.Service.SetEntryLocation("b", doomed.Id);
        stack.Service.SetEntryLocation("file", kept.Id);

        stack.Service.DeleteLocation(doomed.Id);

        Assert.Null(stack.Service.GetEntryOwnLocationId("media"));
        Assert.Null(stack.Service.GetEntryOwnLocationId("b"));
        Assert.Equal(kept.Id, stack.Service.GetEntryEffectiveLocationId("file"));
        Assert.DoesNotContain(stack.Service.GetLocations(), location => location.Id == doomed.Id);
        Assert.Throws<LocationNotFoundException>(() => stack.Service.DeleteLocation(doomed.Id));
    }

    [Fact]
    public void Delete_RemovesAnInheritedLocationWithoutTouchingOtherOverrides()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", Entry("media", type: CatalogDocumentType.Media));
        stack.Catalog.Save("file", Entry("file", "media"));
        var garage = stack.Service.CreateLocation("Garage");
        var shelf = stack.Service.CreateLocation("Shelf");
        stack.Service.SetEntryLocation("media", garage.Id);
        stack.Service.SetEntryLocation("file", shelf.Id);

        stack.Service.DeleteLocation(garage.Id);

        Assert.Null(stack.Service.GetEntryEffectiveLocationId("media"));
        Assert.Equal(shelf.Id, stack.Service.GetEntryEffectiveLocationId("file"));
    }

    [Fact]
    public void RemoveMissingLocationReferences_CleansDanglingAssignments()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        var kept = stack.Service.CreateLocation("Kept");
        stack.Service.SetEntryLocation("a", kept.Id);

        var entry = stack.Catalog.Get("a")!;
        var tampered = new PropertiesCollection(entry.Properties)
        {
            ["LocationId"] = "loc-ghost"
        };
        stack.Catalog.Save("a", new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            ParentId = entry.ParentId,
            Type = entry.Type,
            Size = entry.Size,
            ChildrenCount = entry.ChildrenCount,
            ContentsState = entry.ContentsState,
            Status = entry.Status,
            Properties = tampered
        });

        Assert.Equal(1, stack.Service.RemoveMissingLocationReferences());
        Assert.Null(stack.Service.GetEntryOwnLocationId("a"));
        Assert.Equal(0, stack.Service.RemoveMissingLocationReferences());
        Assert.Equal("Kept", stack.Service.GetLocations().Single().Name);
    }

    [Fact]
    public void CanonicalizeImportedLocation_MapsSourceKeysAndKeepsThemForRoundTrip()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", new CatalogDocument
        {
            Id = "a",
            Name = "a",
            Type = CatalogDocumentType.File,
            Properties = new PropertiesCollection(new Dictionary<string, object?>
            {
                ["PhysicalLocation"] = "Shelf B"
            })
        });

        var name = stack.Service.CanonicalizeImportedLocation(stack.Catalog.Get("a")!);

        Assert.Equal("Shelf B", name);
        Assert.Equal("loc-shelf-b", stack.Service.GetEntryOwnLocationId("a"));
        Assert.Equal("Shelf B", stack.Catalog.Get("a")!.Properties["PhysicalLocation"]?.ToString());
        Assert.Equal("Shelf B", stack.Service.GetLocations().Single().Name);

        // Normalizing again is a no-op once an assignment exists.
        Assert.Equal(0, stack.Service.NormalizeAllEntries());
    }

    [Fact]
    public void LocationId_IsAUserAnnotationAndSurvivesRescanMerge()
    {
        Assert.True(MediaAnnotationPolicy.IsUserAnnotation("LocationId"));

        var staged = new[]
        {
            new CatalogDocument
            {
                Id = "media",
                Name = "media",
                Type = CatalogDocumentType.Media,
                Properties = new PropertiesCollection(new Dictionary<string, object?>
                {
                    ["Title"] = "media"
                })
            }
        };
        var existing = new[]
        {
            new CatalogDocument
            {
                Id = "media",
                Name = "media",
                Type = CatalogDocumentType.Media,
                Properties = new PropertiesCollection(new Dictionary<string, object?>
                {
                    ["Title"] = "media",
                    ["LocationId"] = "loc-garage"
                })
            }
        };

        var merged = new CatalogEntryMerger().Merge(staged, existing);

        Assert.Equal("loc-garage", merged.Single().Properties["LocationId"]?.ToString());
    }
}
