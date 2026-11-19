using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class LocationManagementViewTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-location-ui-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");
    private DryIoc.Container? container;

    public LocationManagementViewTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        container?.Dispose();
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

    private sealed record Fixture(
        RepositoryManager Repositories,
        CatalogDocumentRepository Catalog,
        LocationDocumentRepository Locations,
        CatalogLocationService Service,
        MainWindowViewModel ViewModel,
        string GarageId,
        string ShelfId);

    private Fixture CreateFixture()
    {
        var created = new DryIoc.Container();
        container?.Dispose();
        container = created;
        new CouchbaseServiceRegistrator().RegisterServices(created);
        var repositories = created.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        var locations = (LocationDocumentRepository)repositories.For<LocationDocument>();

        Save(catalog, new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "scifi", Name = "Sci-Fi", ParentId = "movies", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "interstellar", Name = "Interstellar.mkv", ParentId = "scifi", Type = CatalogDocumentType.File, Size = 4096, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "odyssey", Name = "Odyssey.mp4", ParentId = "scifi", Type = CatalogDocumentType.File, Size = 2048, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "drama", Name = "Drama", ParentId = "movies", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });

        var service = new CatalogLocationService(catalog, locations);
        var garage = service.CreateLocation("Garage");
        var shelf = service.CreateLocation("Shelf");
        service.SetEntryLocation("scifi", garage.Id);
        service.SetEntryLocation("drama", shelf.Id);

        var viewModel = new MainWindowViewModel(repositories);
        return new Fixture(repositories, catalog, locations, service, viewModel, garage.Id, shelf.Id);
    }

    private static void Save(CatalogDocumentRepository catalog, CatalogDocument document)
    {
        catalog.Save(document.Id, document);
    }

    private static BrowserTreeNode FindNode(MainWindowViewModel viewModel, string key) =>
        Flatten(viewModel.TreeNodes).First(node => node.Key == key);

    private static IEnumerable<BrowserTreeNode> Flatten(IEnumerable<BrowserTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaFact]
    public void ManagerDialog_CreateRenameMergeDelete_Persist()
    {
        var fixture = CreateFixture();
        var undo = new CatalogUndoManager(fixture.Catalog);
        var dialog = new LocationsDialogViewModel(
            fixture.Service, undo, fixture.Catalog, fixture.Locations);
        Assert.True(dialog.LocationsAvailable);
        Assert.Equal(2, dialog.Locations.Count);
        Assert.Equal(3, dialog.Locations.Single(location => location.LocationId == fixture.GarageId).UsageCount);

        dialog.NewLocationName = "Attic";
        dialog.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var attic = Assert.Single(dialog.Locations, location => location.Name == "Attic");

        dialog.SelectedLocation = attic;
        Dispatcher.UIThread.RunJobs();
        dialog.RenameTo = "Loft";
        dialog.RenameLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(dialog.Locations, location => location.Name == "Loft");

        dialog.MergeTarget = dialog.Locations.Single(location => location.LocationId == fixture.GarageId);
        dialog.MergeLocationsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(dialog.Locations, location => location.Name == "Loft");

        dialog.SelectedLocation = dialog.Locations.Single(location => location.LocationId == fixture.ShelfId);
        Dispatcher.UIThread.RunJobs();
        dialog.DeleteLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(dialog.Locations, location => location.Name == "Shelf");
        Assert.Null(fixture.Service.GetEntryOwnLocationId("drama"));
        Assert.Contains("Garage", dialog.Locations.Select(location => location.Name));

        using var second = new MainWindowViewModel(fixture.Repositories);
        Assert.Contains(fixture.Service.GetLocations(), location => location.Name == "Garage");
        _ = second;
    }

    [AvaloniaFact]
    public void ManagerDialog_ShowItemsUsesSearchContractAndNavigates()
    {
        var fixture = CreateFixture();
        CatalogDocument? navigated = null;
        var dialog = new LocationsDialogViewModel(
            fixture.Service,
            new CatalogUndoManager(fixture.Catalog),
            fixture.Catalog,
            fixture.Locations,
            navigate: document => navigated = document);

        dialog.SelectedLocation = dialog.Locations.Single(location => location.LocationId == fixture.GarageId);
        Dispatcher.UIThread.RunJobs();
        dialog.ShowItemsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // The folder carries the assignment and both children inherit it.
        Assert.Equal(3, dialog.FilterResults.Count);
        Assert.Contains(dialog.FilterResults, match => match.Document.Id == "interstellar");
        Assert.Contains(dialog.FilterResults, match => match.Document.Id == "odyssey");
        Assert.Contains(dialog.FilterResults, match => match.Document.Id == "scifi");

        dialog.SelectedResult = dialog.FilterResults.First(match => match.Document.Id == "interstellar");
        Dispatcher.UIThread.RunJobs();
        dialog.NavigateToSelectedCommand.Execute(null);
        Assert.Equal("interstellar", navigated?.Id);
    }

    [AvaloniaFact]
    public void ManagerDialog_CancelLeavesAppliedChangesAndStaysUnaccepted()
    {
        var fixture = CreateFixture();
        var dialog = new LocationsDialogViewModel(
            fixture.Service, new CatalogUndoManager(fixture.Catalog), fixture.Catalog, fixture.Locations);

        Assert.False(dialog.DialogAccepted);

        dialog.NewLocationName = "Basement";
        dialog.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Closing without confirming reports "not accepted" but changes already applied, mirroring the
        // tags manager: definition edits save immediately and closing changes nothing further.
        Assert.False(dialog.DialogAccepted);
        Assert.Contains(fixture.Service.GetLocations(), location => location.Name == "Basement");

        dialog.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.DialogAccepted);
    }

    [AvaloniaFact]
    public void OpenLocationsDialogCommand_RaisesRequestWithPopulatedDialog()
    {
        var fixture = CreateFixture();
        LocationsDialogViewModel? dialog = null;
        bool? completed = null;
        fixture.ViewModel.LocationsRequested += (_, args) =>
        {
            dialog = args.Dialog;
            args.Complete(false);
            completed = false;
        };

        fixture.ViewModel.SelectedTreeNode = FindNode(fixture.ViewModel, "scifi");
        Dispatcher.UIThread.RunJobs();
        fixture.ViewModel.OpenLocationsDialogCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog);
        Assert.Equal(2, dialog.Locations.Count);
        Assert.False(completed);
    }

    [AvaloniaFact]
    public void LocationFilter_FiltersBrowserItemsByEffectiveLocation()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.SelectedTreeNode = FindNode(fixture.ViewModel, "movies");
        Dispatcher.UIThread.RunJobs();

        fixture.ViewModel.SetLocationFilterCommand.Execute(fixture.GarageId);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Sci-Fi"], fixture.ViewModel.BrowserItems.Select(item => item.Name).ToArray());

        fixture.ViewModel.SetLocationFilterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, fixture.ViewModel.BrowserItems.Count);
    }
}
