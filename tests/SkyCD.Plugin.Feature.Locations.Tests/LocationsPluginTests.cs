using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Catalog.Locations;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Menu;
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
    public void EnabledPluginContributesLocationColumnSearchFieldAndConfigurationPage()
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

        Assert.IsNotAssignableFrom<IMenuPluginCapability>(plugin);
        var configuration = Assert.IsAssignableFrom<IPluginConfigurationCapability>(plugin);
        Assert.Equal("locations", Assert.Single(configuration.Configurations).ConfigurationId);
        Assert.NotEmpty(plugin.GetTranslations());
    }

    [Fact]
    public async Task CreateConfigurationPage_WithoutLocationApi_ThrowsInvalidOperation()
    {
        var plugin = new LocationsPlugin();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            plugin.CreatePageAsync(new PluginConfigurationRequest { ConfigurationId = "locations" }));
        Assert.Contains("host location API", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigurationPageUsesTheTranslatedTitleAndEmbedsTheManagerView()
    {
        const string translationNamespace = "plugins.skycd.locations";
        using var stack = new Stack();
        var plugin = stack.CreatePlugin();
        LocalizationService.Current.Register(translationNamespace, plugin);
        try
        {
            Assert.Equal("Locations", Assert.Single(plugin.Configurations).Title);

            var page = await plugin.CreatePageAsync(new PluginConfigurationRequest { ConfigurationId = "locations" });
            var view = Assert.IsType<LocationsView>(page.Content);
            var viewModel = Assert.IsType<LocationsManagerViewModel>(view.DataContext);
            Assert.True(viewModel.IsCatalogOpen);
        }
        finally
        {
            LocalizationService.Current.Unregister(translationNamespace);
        }
    }

    [Fact]
    public async Task ConfigurationPageKeepsStoredSettingsOnAccept()
    {
        using var stack = new Stack();
        var plugin = stack.CreatePlugin();
        var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = "locations",
            CurrentSettings = new Dictionary<string, string?> { ["kept"] = "value" }
        });

        var result = await page.AcceptAsync();

        Assert.True(result.Success);
        Assert.Equal("value", result.Settings["kept"]);
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
    public void LocationsManager_InlineRenameMergeAndDelete_CommitImmediately()
    {
        const string translationNamespace = "plugins.skycd.locations";
        LocalizationService.Current.Register(translationNamespace, new LocationsPlugin());
        try
        {
            using var stack = new Stack();
            var viewModel = new LocationsManagerViewModel(stack.Api);

            // Adding opens an unstored inline editor and creates only on commit.
            viewModel.AddLocationCommand.Execute(null);
            var draft = Assert.Single(viewModel.Locations);
            Assert.True(draft.IsEditing);
            draft.EditName = "  Garage  ";
            Assert.True(viewModel.CommitLocationRename(draft));
            Assert.Equal("Garage", Assert.Single(stack.Api.GetLocations()).Name);

            // Inline rename commits through the API.
            var garage = viewModel.Locations.Single();
            viewModel.BeginLocationEdit(garage);
            Assert.True(garage.IsEditing);
            garage.EditName = "Shelf A";
            Assert.True(viewModel.CommitLocationRename(garage));
            Assert.Equal("Shelf A", Assert.Single(stack.Api.GetLocations()).Name);

            // Cancelling keeps the committed name.
            var shelf = viewModel.Locations.Single();
            viewModel.BeginLocationEdit(shelf);
            shelf.EditName = "Not committed";
            viewModel.CancelLocationRename(shelf);
            Assert.False(shelf.IsEditing);
            Assert.Equal("Shelf A", Assert.Single(stack.Api.GetLocations()).Name);

            // A second location can be merged into the selected destination.
            viewModel.AddLocationCommand.Execute(null);
            var atticDraft = viewModel.Locations.Single(location => location.Id != shelf.Id);
            atticDraft.EditName = "Attic";
            Assert.True(viewModel.CommitLocationRename(atticDraft));

            var source = viewModel.Locations.Single(location => location.Name == "Attic");
            var target = viewModel.Locations.Single(location => location.Name == "Shelf A");
            viewModel.SelectedLocation = source;
            viewModel.ConfirmMergeLocations(source, target);
            Assert.Single(stack.Api.GetLocations());
            Assert.Contains("Attic", viewModel.StatusText);

            // Deleting keeps the in-use warning message.
            var garageWithUsage = stack.Api.CreateLocation("Garage");
            stack.Api.SetEntryLocation("media", garageWithUsage.Id);
            var deleteViewModel = new LocationsManagerViewModel(stack.Api);
            deleteViewModel.SelectedLocation =
                deleteViewModel.Locations.Single(location => location.Id == garageWithUsage.Id);
            var usageCount = deleteViewModel.SelectedLocation!.UsageCount;
            Assert.True(usageCount > 0);
            deleteViewModel.DeleteLocationCommand.Execute(null);
            Assert.Contains(usageCount.ToString(), deleteViewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Current.Unregister(translationNamespace);
        }
    }

    [Fact]
    public void ManagerReportsCatalogUnavailableWithoutAnOpenCatalog()
    {
        var state = new OpenCatalogState();
        using var api = new LocationApiAdapter(state);
        var viewModel = new LocationsManagerViewModel(api);

        Assert.False(viewModel.IsCatalogOpen);
        Assert.True(viewModel.ShowCatalogUnavailable);
        Assert.Empty(viewModel.Locations);
        Assert.False(viewModel.AddLocationCommand.CanExecute(null));
        Assert.False(viewModel.DeleteLocationCommand.CanExecute(null));
        Assert.False(viewModel.MergeLocationsCommand.CanExecute(null));
    }

    [Fact]
    public void MergeLocationsDialog_RequiresADestinationAndExposesTheSourceSummary()
    {
        const string translationNamespace = "plugins.skycd.locations";
        LocalizationService.Current.Register(translationNamespace, new LocationsPlugin());
        try
        {
            var source = new LocationRow("source", "Garage", 3);
            var destinations = new[]
            {
                new LocationRow("attic", "Attic", 0),
                new LocationRow("shelf", "Shelf", 0)
            };
            var dialog = new MergeLocationsDialogViewModel(source, destinations);

            Assert.Equal("Garage", dialog.SourceName);
            Assert.Equal(3, dialog.UsageCount);
            Assert.Equal(2, dialog.Destinations.Count);
            Assert.Null(dialog.SelectedDestination);
            Assert.False(dialog.CanConfirm);

            dialog.SelectedDestination = destinations[1];
            Assert.True(dialog.CanConfirm);

            dialog.SelectedDestination = null;
            Assert.False(dialog.CanConfirm);
        }
        finally
        {
            LocalizationService.Current.Unregister(translationNamespace);
        }
    }

    [Fact]
    public void ManagerDiscardsUnstoredDraftWhenDeleted()
    {
        using var stack = new Stack();
        var viewModel = new LocationsManagerViewModel(stack.Api);

        viewModel.AddLocationCommand.Execute(null);
        Assert.Single(viewModel.Locations);
        Assert.True(viewModel.DeleteLocationCommand.CanExecute(null));

        viewModel.DeleteLocationCommand.Execute(null);

        Assert.Empty(viewModel.Locations);
        Assert.Null(viewModel.SelectedLocation);
        Assert.Empty(viewModel.ErrorText);
        Assert.Empty(stack.Api.GetLocations());
    }

    [Fact]
    public void CommittingInlineRenameKeepsTheNewlySelectedRow()
    {
        const string translationNamespace = "plugins.skycd.locations";
        LocalizationService.Current.Register(translationNamespace, new LocationsPlugin());
        try
        {
            using var stack = new Stack();
            var garage = stack.Api.CreateLocation("Garage");
            var attic = stack.Api.CreateLocation("Attic");
            var viewModel = new LocationsManagerViewModel(stack.Api);

            var garageRow = viewModel.Locations.Single(location => location.Id == garage.Id);
            var atticRow = viewModel.Locations.Single(location => location.Id == attic.Id);

            // The list commits an open editor when the selection moves to another row.
            viewModel.SelectedLocation = garageRow;
            viewModel.BeginLocationEdit(garageRow);
            garageRow.EditName = "Garage 2";
            viewModel.SelectedLocation = atticRow;

            Assert.True(viewModel.CommitLocationRename(garageRow));

            Assert.Equal(atticRow.Id, viewModel.SelectedLocation?.Id);
            Assert.Equal("Garage 2",
                stack.Api.GetLocations().Single(location => location.Id == garage.Id).Name);
        }
        finally
        {
            LocalizationService.Current.Unregister(translationNamespace);
        }
    }

    [Fact]
    public void MergeDestinationsExcludeTheSourceAndUnstoredRows()
    {
        using var stack = new Stack();
        var garage = stack.Api.CreateLocation("Garage");
        var attic = stack.Api.CreateLocation("Attic");
        var viewModel = new LocationsManagerViewModel(stack.Api);
        viewModel.AddLocationCommand.Execute(null);

        var source = viewModel.Locations.Single(location => location.Id == garage.Id);
        var destination = Assert.Single(viewModel.GetMergeDestinations(source));

        Assert.Equal(attic.Id, destination.Id);
        Assert.DoesNotContain(viewModel.GetMergeDestinations(source), location => !location.IsStored);
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
