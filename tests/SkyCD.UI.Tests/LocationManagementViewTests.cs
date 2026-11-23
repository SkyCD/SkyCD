using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DryIoc;
using SkyCD.App.Views;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
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

    private static OptionsDialogViewModel OpenLocationsOptions(MainWindowViewModel viewModel)
    {
        var options = new OptionsDialogViewModel(["English"]);
        options.SetLocations(viewModel.GetLocationSnapshots());
        return options;
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

    private static string[] StoredNames(LocationDocumentRepository locations) =>
        locations.GetOrdered().Select(location => location.Name).ToArray();

    [AvaloniaFact]
    public void OptionsLocations_CreateRenameMergeDelete_ApplyOnSave()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        // The page lists the stored locations with effective usage counts (folder plus inheriting files).
        Assert.Equal(["Garage", "Shelf"], options.Locations.Select(location => location.Name).ToArray());
        Assert.Equal(3, options.Locations.Single(location => location.LocationId == fixture.GarageId).UsageCount);
        Assert.Equal(1, options.Locations.Single(location => location.LocationId == fixture.ShelfId).UsageCount);

        // "New location" appends an unstored row that edits its name in place and stages nothing yet.
        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var attic = Assert.Single(options.Locations, location => location.IsEditing);
        Assert.False(attic.IsStored);
        Assert.Empty(attic.Name);
        Assert.True(options.BuildLocationChangeSet().IsEmpty);

        attic.EditName = "Attic";
        Assert.True(options.CommitLocationRename(attic));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Attic", attic.Name);
        Assert.False(attic.IsEditing);

        // Renaming an unstored row replaces its pending creation name instead of staging a rename.
        options.SelectedLocation = attic;
        Dispatcher.UIThread.RunJobs();
        options.BeginLocationEdit(attic);
        attic.EditName = "Loft";
        Assert.True(options.CommitLocationRename(attic));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(options.Locations, location => location.Name == "Loft");
        Assert.DoesNotContain(options.BuildLocationChangeSet().Changes,
            change => change is LocationChange.Rename { LocationId: var id } && id == attic.LocationId);
        Assert.Single(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Create { Name: "Loft" });

        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);

        // Unsaved rows have no stored definition, so they cannot be merged away yet.
        options.ConfirmMergeLocations(attic, garage);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.LocationUnsaved), options.LocationsStatusText);
        Assert.DoesNotContain(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Merge);

        // Deleting an unsaved row drops its pending creation.
        options.SelectedLocation = attic;
        Dispatcher.UIThread.RunJobs();
        options.DeleteLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(options.Locations, location => location.Name == "Loft");
        Assert.DoesNotContain(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Create);

        // A row created in this session is only created on save, under the name it carries then.
        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var loft = Assert.Single(options.Locations, location => location.IsEditing);
        loft.EditName = "Attic";
        Assert.True(options.CommitLocationRename(loft));
        options.BeginLocationEdit(loft);
        loft.EditName = "Loft";
        Assert.True(options.CommitLocationRename(loft));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(options.Locations, location => location.Name == "Loft");
        Assert.Contains(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Create { Name: "Loft" });

        var shelf = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        options.SelectedLocation = shelf;
        Dispatcher.UIThread.RunJobs();
        options.ConfirmMergeLocations(shelf, garage);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(options.Locations, location => location.Name == "Shelf");
        Assert.Equal(4, garage.UsageCount);
        Assert.Equal(LocalizationService.Current.Format(TextKeys.LocationMerged, "Shelf", "Garage"), options.LocationsStatusText);

        // With the merged row gone from the list, the remaining target becomes the selection.
        Assert.DoesNotContain(options.Locations, location => location.LocationId == fixture.ShelfId);
        Assert.Same(garage, options.SelectedLocation);

        // Nothing reached the store until the dialog is saved.
        Assert.Equal(["Garage", "Shelf"], StoredNames(fixture.Locations));

        fixture.ViewModel.ApplyLocationChanges(options.BuildLocationChangeSet());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Garage", "Loft"], StoredNames(fixture.Locations));
        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("scifi"));
        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("drama"));
        Assert.Equal(2, fixture.ViewModel.GetLocationSnapshots().Count);

        // The location menus and filters pick the new definitions up right away.
        var locationsMenu = Assert.Single(fixture.ViewModel.BrowserContextMenuItems, item => item.Key == "edit_locations");
        Assert.Contains(locationsMenu.Items, item => item.Header == "Garage (4)");
        Assert.Contains(locationsMenu.Items, item => item.Header == "Loft (0)");
    }

    [AvaloniaFact]
    public void OptionsLocations_InlineRename_CommitsAndCancels()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        options.SelectedLocation = garage;
        Dispatcher.UIThread.RunJobs();
        options.BeginLocationEdit(garage);
        Assert.True(garage.IsEditing);
        Assert.Equal("Garage", garage.EditName);

        garage.EditName = "Carport";
        Assert.True(options.CommitLocationRename(garage));
        Assert.False(garage.IsEditing);
        Assert.Equal("Carport", garage.Name);
        Assert.Contains(options.BuildLocationChangeSet().Changes,
            change => change is LocationChange.Rename { LocationId: var id, NewName: "Carport" } && id == fixture.GarageId);

        // Cancelling drops the draft, restores the committed name and stages nothing new.
        options.BeginLocationEdit(garage);
        garage.EditName = "Temporary";
        options.CancelLocationRename(garage);
        Assert.False(garage.IsEditing);
        Assert.Equal("Carport", garage.Name);
        Assert.Single(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Rename);

        // Empty and duplicate names keep the editor open with the existing validation messages.
        var shelf = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        options.BeginLocationEdit(shelf);
        shelf.EditName = "   ";
        Assert.False(options.CommitLocationRename(shelf));
        Assert.True(shelf.IsEditing);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.LocationNameRequired), options.LocationsStatusText);

        shelf.EditName = "CARPORT";
        Assert.False(options.CommitLocationRename(shelf));
        Assert.True(shelf.IsEditing);
        Assert.Equal(new LocationDuplicateNameException("CARPORT").Message, options.LocationsStatusText);

        // Only one row edits at a time; opening another editor leaves the previous name untouched.
        options.BeginLocationEdit(garage);
        Assert.False(shelf.IsEditing);
        Assert.Equal("Shelf", shelf.Name);
        Assert.True(garage.IsEditing);
    }

    [AvaloniaFact]
    public void OptionsLocations_NewLocationStartsInlineAndCancelDropsUnnamedRow()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var created = Assert.Single(options.Locations, location => location.IsEditing);
        Assert.False(created.IsStored);
        Assert.Empty(created.Name);
        Assert.True(options.BuildLocationChangeSet().IsEmpty);
        Assert.False(options.MergeLocationsCommand.CanExecute(null));

        options.CancelLocationRename(created);
        Assert.DoesNotContain(created, options.Locations);
        Assert.True(options.BuildLocationChangeSet().IsEmpty);
        Assert.Equal("Garage", options.Locations[0].Name);

        // Starting another new row abandons the previous unnamed one.
        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var first = Assert.Single(options.Locations, location => location.IsEditing);
        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var second = Assert.Single(options.Locations, location => location.IsEditing);

        Assert.NotSame(first, second);
        Assert.DoesNotContain(first, options.Locations);
    }

    [AvaloniaFact]
    public void OptionsLocations_MergeDialogStagesOnlyOnConfirm()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        // An unstored row is never offered as a merge destination.
        options.CreateLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var shelf = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        options.SelectedLocation = shelf;
        Dispatcher.UIThread.RunJobs();

        var destinations = options.GetMergeDestinations(shelf);
        var destination = Assert.Single(destinations);
        Assert.Same(garage, destination);

        var dialog = new MergeLocationsDialogViewModel(shelf, destinations);
        Assert.Equal("Shelf", dialog.SourceName);
        Assert.Equal(1, dialog.UsageCount);
        Assert.Contains("Shelf", dialog.Summary, StringComparison.Ordinal);
        Assert.False(dialog.CanConfirm);

        dialog.SelectedDestination = destination;
        Assert.True(dialog.CanConfirm);

        // Dismissing the dialog without confirming leaves the staged changes untouched.
        Assert.DoesNotContain(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Merge);

        // Confirming stages the merge and moves the usage onto the destination.
        options.ConfirmMergeLocations(shelf, dialog.SelectedDestination!);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(options.BuildLocationChangeSet().Changes,
            change => change is LocationChange.Merge { SourceId: var sourceId, TargetId: var targetId } &&
                      sourceId == fixture.ShelfId && targetId == fixture.GarageId);
        Assert.Equal(4, garage.UsageCount);
        Assert.DoesNotContain(options.Locations, location => location.LocationId == fixture.ShelfId);
        Assert.Same(garage, options.SelectedLocation);
    }

    [AvaloniaFact]
    public void MergeLocationsWindow_LoadsWithDestinationChoices()
    {
        var source = new LocationItemViewModel("shelf", "Shelf", 1);
        var destination = new LocationItemViewModel("garage", "Garage", 3);

        var window = new MergeLocationsWindow(new MergeLocationsDialogViewModel(source, [destination]));

        var viewModel = Assert.IsType<MergeLocationsDialogViewModel>(window.DataContext);
        Assert.Equal("Shelf", viewModel.SourceName);
        Assert.Same(destination, Assert.Single(viewModel.Destinations));
        Assert.False(viewModel.CanConfirm);
    }

    [AvaloniaFact]
    public void OptionsLocations_MergeReassignsEntriesToTarget()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        var shelf = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        options.SelectedLocation = shelf;
        Dispatcher.UIThread.RunJobs();
        options.ConfirmMergeLocations(shelf, garage);
        Dispatcher.UIThread.RunJobs();

        fixture.ViewModel.ApplyLocationChanges(options.BuildLocationChangeSet());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("drama"));
        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("scifi"));
        Assert.Equal(["Garage"], StoredNames(fixture.Locations));
    }

    [AvaloniaFact]
    public void OptionsLocations_RenameAndMerge_RecordUndoActions()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        options.SelectedLocation = garage;
        Dispatcher.UIThread.RunJobs();
        options.BeginLocationEdit(garage);
        garage.EditName = "Carport";
        Assert.True(options.CommitLocationRename(garage));

        var shelf = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        options.SelectedLocation = shelf;
        Dispatcher.UIThread.RunJobs();
        options.ConfirmMergeLocations(shelf, garage);
        Dispatcher.UIThread.RunJobs();

        fixture.ViewModel.ApplyLocationChanges(options.BuildLocationChangeSet());
        Dispatcher.UIThread.RunJobs();

        Assert.True(fixture.ViewModel.CanUndo);
        Assert.Equal(["Carport"], StoredNames(fixture.Locations));
        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("scifi"));
    }

    [AvaloniaFact]
    public void OptionsLocations_Cancel_DiscardsStagedChanges()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        options.CreateLocationCommand.Execute(null);
        var basement = Assert.Single(options.Locations, location => location.IsEditing);
        basement.EditName = "Basement";
        Assert.True(options.CommitLocationRename(basement));

        var garage = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        options.SelectedLocation = garage;
        Dispatcher.UIThread.RunJobs();
        options.BeginLocationEdit(garage);
        garage.EditName = "Garage 2";
        Assert.True(options.CommitLocationRename(garage));

        options.SelectedLocation = options.Locations.Single(location => location.LocationId == fixture.ShelfId);
        Dispatcher.UIThread.RunJobs();
        options.DeleteLocationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Cancelling never calls the host, so neither the definitions nor the assignments may move.
        Assert.False(options.DialogAccepted);
        Assert.Equal(["Garage", "Shelf"], StoredNames(fixture.Locations));
        Assert.Equal(fixture.GarageId, fixture.Service.GetEntryOwnLocationId("scifi"));
        Assert.Equal(fixture.ShelfId, fixture.Service.GetEntryOwnLocationId("drama"));
        Assert.False(fixture.ViewModel.IsDirtyDocument);

        // An empty change set is also a no-op when the dialog happens to be accepted without edits.
        var unchanged = OpenLocationsOptions(fixture.ViewModel);
        Assert.True(unchanged.BuildLocationChangeSet().IsEmpty);
        fixture.ViewModel.ApplyLocationChanges(unchanged.BuildLocationChangeSet());
        Assert.Equal(["Garage", "Shelf"], StoredNames(fixture.Locations));
    }

    [AvaloniaFact]
    public void OptionsLocations_ValidatesAgainstStagedList()
    {
        var fixture = CreateFixture();
        var options = OpenLocationsOptions(fixture.ViewModel);
        Dispatcher.UIThread.RunJobs();

        options.CreateLocationCommand.Execute(null);
        var created = Assert.Single(options.Locations, location => location.IsEditing);
        created.EditName = "   ";
        Assert.False(options.CommitLocationRename(created));
        Assert.True(created.IsEditing);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.LocationNameRequired), options.LocationsStatusText);

        created.EditName = "SHELF";
        Assert.False(options.CommitLocationRename(created));
        Assert.True(created.IsEditing);
        Assert.Equal("A location named 'SHELF' already exists. Location names are unique ignoring case.", options.LocationsStatusText);
        Assert.Equal(3, options.Locations.Count);

        created.EditName = "Attic";
        Assert.True(options.CommitLocationRename(created));

        // A staged name is taken too, before anything is stored.
        options.CreateLocationCommand.Execute(null);
        var duplicate = Assert.Single(options.Locations, location => location.IsEditing);
        duplicate.EditName = "Attic";
        Assert.False(options.CommitLocationRename(duplicate));
        Assert.Equal(new LocationDuplicateNameException("Attic").Message, options.LocationsStatusText);
        Assert.Equal(4, options.Locations.Count);
        Assert.Single(options.BuildLocationChangeSet().Changes, change => change is LocationChange.Create { Name: "Attic" });

        options.CancelLocationRename(duplicate);
        Assert.Equal(3, options.Locations.Count);

        // Deleting a location that is still in use reports how many entries it has.
        options.SelectedLocation = options.Locations.Single(location => location.LocationId == fixture.GarageId);
        Dispatcher.UIThread.RunJobs();
        options.DeleteLocationCommand.Execute(null);
        Assert.Equal(LocalizationService.Current.Format(TextKeys.LocationDeletedInUse, "Garage", 3), options.LocationsStatusText);
        Assert.Contains(options.BuildLocationChangeSet().Changes,
            change => change is LocationChange.Delete { LocationId: var id } && id == fixture.GarageId);
        Assert.Equal(["Garage", "Shelf"], StoredNames(fixture.Locations));
    }

    [AvaloniaFact]
    public void OpenLocationsDialogCommand_OpensOptionsWithLocationsCategory()
    {
        var fixture = CreateFixture();
        OptionsDialogRequestedEventArgs? request = null;
        fixture.ViewModel.OptionsRequested += (_, args) =>
        {
            request = args;
            args.Dialog.SetLocations(fixture.ViewModel.GetLocationSnapshots());
        };

        fixture.ViewModel.SelectedTreeNode = FindNode(fixture.ViewModel, "scifi");
        Dispatcher.UIThread.RunJobs();
        fixture.ViewModel.OpenLocationsDialogCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(request);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Locations), request!.PreselectSettingCategory);
        request.Dialog.SelectedSettingCategory = request.Dialog.SettingCategories.Single(
            category => category.Title == request.PreselectSettingCategory);
        Assert.True(request.Dialog.IsLocationsCategorySelected);
        Assert.True(request.Dialog.ShowLocationsSection);

        // The Tools toolbar command and the Locations context submenu keep their labels and open the same page.
        var toolbarLocation = Assert.Single(fixture.ViewModel.ToolbarCommands(), command => command.Id == "locations");
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ManageLocations), toolbarLocation.Label);
        Assert.Same(fixture.ViewModel.OpenLocationsDialogCommand, toolbarLocation.Command);

        var locationsMenu = Assert.Single(fixture.ViewModel.BrowserContextMenuItems, item => item.Key == "edit_locations");
        var manageItem = Assert.Single(locationsMenu.Items, item => item.Header == LocalizationService.Current.Get(UiKeys.ManageLocations));
        Assert.Same(fixture.ViewModel.OpenLocationsDialogCommand, manageItem.Command);
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
