using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.Plugin.Feature.Locations.Models;
using SkyCD.Plugin.Feature.Locations.Views;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Presentation.Catalog;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.Plugin.Feature.Locations.Tests;

public sealed class LocationsPluginTests
{
    [Fact]
    public void EnabledPluginContributesLocationColumnSearchFieldAndMenu()
    {
        using var stack = new Stack();
        var garage = stack.Api.CreateLocation("Garage");
        stack.Api.SetEntryLocation("media", garage.Id);

        var plugin = stack.CreatePlugin();
        var features = new CatalogFeatureExtensionManager([plugin]);

        var column = Assert.Single(features.GetDetailsColumns());
        Assert.Equal("location", column.Key);
        Assert.Equal("Garage", column.SelectValue(Entry("file")));
        Assert.Null(column.SelectValue(Entry("unmapped")));

        var field = Assert.Single(features.GetAdvancedSearchFields());
        Assert.Equal("location", field.Key);
        Assert.True(field.Matches(Entry("file")));
        Assert.False(field.Matches(Entry("unmapped")));
        Assert.Equal("Garage", field.SelectValue?.Invoke(Entry("file")));

        var menu = Assert.Single(plugin.GetMenuContributions());
        Assert.Equal("skycd.locations.manage", menu.CommandId);
        Assert.Equal("Tools", menu.Location);
        Assert.NotEmpty(plugin.GetTranslations());
    }

    [Fact]
    public void InheritanceResolvesAncestorLocationForColumnAndSearch()
    {
        using var stack = new Stack();
        var plugin = stack.CreatePlugin();
        var features = new CatalogFeatureExtensionManager([plugin]);
        var column = Assert.Single(features.GetDetailsColumns());

        var garage = stack.Api.CreateLocation("Garage");
        stack.Api.SetEntryLocation("media", garage.Id);

        Assert.Equal("Garage", column.SelectValue(Entry("folder")));
        Assert.Equal("Garage", column.SelectValue(Entry("file")));
        Assert.Equal("Garage", stack.Api.GetEffectiveLocationName("media"));
        Assert.Equal("Garage", stack.Api.GetEffectiveLocationName("file"));
    }

    [Fact]
    public void MergeMovesAssignmentsAndDeleteClearsThem()
    {
        using var stack = new Stack();
        var garage = stack.Api.CreateLocation("Garage");
        var attic = stack.Api.CreateLocation("Attic");
        stack.Api.SetEntryLocation("media", garage.Id);
        stack.Api.SetEntryLocation("file", attic.Id);

        stack.Api.MergeLocations(attic.Id, garage.Id);

        var remaining = Assert.Single(stack.Api.GetLocations());
        Assert.Equal(garage.Id, remaining.Id);
        Assert.Equal(3, remaining.UsageCount);
        Assert.Equal("Garage", stack.Api.GetEffectiveLocationName("file"));

        stack.Api.DeleteLocation(garage.Id);

        Assert.Empty(stack.Api.GetLocations());
        Assert.Null(stack.Api.GetEffectiveLocationName("file"));
        Assert.False(stack.Documents.Get("file")!.Properties.ContainsKey("LocationId"));
    }

    [Fact]
    public void DisabledPluginLeavesLocationDataIntact()
    {
        using var workspace = new TestWorkspace();
        string locationName;
        using (var database = OpenDatabase(workspace.Root))
        {
            var documents = OpenDocuments(database);
            var state = new OpenCatalogState();
            state.Attach(documents);
            using var api = new LocationApiAdapter(state);
            var garage = api.CreateLocation("Garage");
            api.SetEntryLocation("media", garage.Id);
            locationName = garage.Name;

            var features = new CatalogFeatureExtensionManager([]);
            Assert.Empty(features.GetDetailsColumns());
            Assert.Empty(features.GetAdvancedSearchFields());
        }

        using (var database = OpenDatabase(workspace.Root))
        {
            var state = new OpenCatalogState();
            state.Attach(OpenDocuments(database));
            using var api = new LocationApiAdapter(state);
            var location = Assert.Single(api.GetLocations());
            Assert.Equal(locationName, location.Name);
            Assert.Equal("Garage", api.GetEffectiveLocationName("file"));
        }
    }

    [Fact]
    public void LocationsManagerValidatesAndAppliesChangesImmediately()
    {
        const string translationNamespace = "plugins.skycd.locations";
        LocalizationService.Current.Register(translationNamespace, new LocationsPlugin());
        try
        {
            using var stack = new Stack();
            var viewModel = new LocationsManagerViewModel(stack.Api);

            viewModel.NewLocationName = "Garage";
            viewModel.CreateLocationCommand.Execute(null);
            Assert.Single(viewModel.Locations);
            Assert.Contains("Garage", viewModel.StatusText);

            viewModel.NewLocationName = "  garage  ";
            viewModel.CreateLocationCommand.Execute(null);
            Assert.NotEmpty(viewModel.ErrorText);
            Assert.Single(viewModel.Locations);

            viewModel.LocationName = "Shelf A";
            viewModel.RenameLocationCommand.Execute(null);
            Assert.Equal("Shelf A", stack.Api.GetLocations().Single().Name);

            viewModel.NewLocationName = "Attic";
            viewModel.CreateLocationCommand.Execute(null);
            Assert.NotNull(viewModel.SelectedLocation);
            Assert.Equal("Attic", viewModel.SelectedLocation!.Name);
            viewModel.MergeTarget = viewModel.MergeTargets.Single();
            viewModel.MergeLocationsCommand.Execute(null);
            Assert.Single(stack.Api.GetLocations());
            Assert.Contains("Attic", viewModel.StatusText);

            var shelf = stack.Api.GetLocations().Single();
            var garage = stack.Api.CreateLocation("Garage");
            stack.Api.SetEntryLocation("media", garage.Id);
            var deleteViewModel = new LocationsManagerViewModel(stack.Api);
            deleteViewModel.SelectedLocation =
                deleteViewModel.Locations.Single(location => location.Id == garage.Id);
            var usageCount = deleteViewModel.SelectedLocation!.UsageCount;
            deleteViewModel.DeleteLocationCommand.Execute(null);
            Assert.Contains(usageCount.ToString(), deleteViewModel.StatusText, StringComparison.Ordinal);
            Assert.Null(stack.Api.GetEffectiveLocationName("file"));
            Assert.Equal(shelf.Id, stack.Api.GetLocations().Single().Id);
        }
        finally
        {
            LocalizationService.Current.Unregister(translationNamespace);
        }
    }

    [Fact]
    public void PluginWithoutLocationApiKeepsContributionsInert()
    {
        var plugin = new LocationsPlugin();
        var features = new CatalogFeatureExtensionManager([plugin]);

        var column = Assert.Single(features.GetDetailsColumns());
        Assert.Null(column.SelectValue(Entry("file")));
        var field = Assert.Single(features.GetAdvancedSearchFields());
        Assert.False(field.Matches(Entry("file")));
        Assert.NotEmpty(plugin.GetTranslations()["en"]);
    }

    private static CatalogEntry Entry(string id) =>
        new(id, id, id, null, CatalogEntryKind.File, 0, new Dictionary<string, string>());

    private static Database OpenDatabase(string directory) =>
        new("locations-plugin", new DatabaseConfiguration { Directory = directory });

    private static CatalogDocumentRepository OpenDocuments(Database database)
    {
        var collection = database.GetDefaultCollection();
        var documents = new CatalogDocumentRepository();
        documents.Initialize(typeof(CatalogDocument), collection.Name, collection);
        var existing = documents.Get("media");
        if (existing is null)
        {
            documents.Save("media", Document("media", null, CatalogDocumentType.Media));
            documents.Save("folder", Document("folder", "media", CatalogDocumentType.Folder));
            documents.Save("file", Document("file", "folder", CatalogDocumentType.File));
        }

        return documents;
    }

    private static CatalogDocument Document(string id, string? parentId, CatalogDocumentType type) =>
        new()
        {
            Id = id,
            Name = id,
            ParentId = parentId,
            Type = type,
            Size = 0,
            Properties = new PropertiesCollection()
        };

    private sealed class Stack : IDisposable
    {
        private readonly TestWorkspace workspace = new();
        private readonly Database database;
        private readonly OpenCatalogState state = new();

        public Stack()
        {
            database = OpenDatabase(workspace.Root);
            Documents = OpenDocuments(database);
            state.Attach(Documents);
            state.Path = "test-catalog";
            Api = new LocationApiAdapter(state);
        }

        public CatalogDocumentRepository Documents { get; }

        public LocationApiAdapter Api { get; }

        public LocationsPlugin CreatePlugin()
        {
            var plugin = new LocationsPlugin();
            plugin.InitializeLocationApi(Api);
            return plugin;
        }

        public void Dispose()
        {
            Api.Dispose();
            state.Attach(null);
            database.Dispose();
            workspace.Dispose();
        }
    }
}
