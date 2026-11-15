using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.UI.Controls.Preview;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers how the main window surfaces the offline preview: browser selection drives the pane, the pane can
/// be toggled from the View menu, and cached metadata reaches the find dialog through the generic field
/// provider rather than through media-specific code in the UI.
/// </summary>
public sealed class MainWindowPreviewPaneTests
{
    private const string MediaRootId = "media-root";
    private const string TrackId = "file-track-1";

    private static readonly CatalogDocument[] Catalog =
    [
        new() { Id = MediaRootId, Name = "Mixtape", Type = CatalogDocumentType.Media },
        new()
        {
            Id = TrackId,
            ParentId = MediaRootId,
            Name = "track.mp3",
            Type = CatalogDocumentType.File,
            Size = 64
        },
        new()
        {
            Id = "file-plain-1",
            ParentId = MediaRootId,
            Name = "readme.txt",
            Type = CatalogDocumentType.File,
            Size = 12
        }
    ];

    [Fact]
    public async Task BrowserSelection_DrivesThePreviewPane()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);

        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog, mediaCacheStore: store);
        await viewModel.Preview.CurrentLoad;

        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single(node => node.Key == MediaRootId);
        viewModel.SelectedBrowserItem = Catalog.Single(document => document.Id == TrackId);
        await viewModel.Preview.CurrentLoad;

        Assert.Equal(MediaPreviewState.Ready, viewModel.Preview.State);
        Assert.Equal("track.mp3", viewModel.Preview.EntryName);
        Assert.Equal(TrackId, viewModel.Preview.CurrentSnapshot?.EntryId);

        var comments = viewModel.Preview.Fields.Single(row => row.Label == "Comments");
        Assert.Equal("captured comment", comments.Value);
    }

    [Fact]
    public async Task EntryWithoutCapturedResults_ShowsTheUnavailableState()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);

        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog, mediaCacheStore: store);
        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single(node => node.Key == MediaRootId);
        viewModel.SelectedBrowserItem = Catalog.Single(document => document.Id == "file-plain-1");
        await viewModel.Preview.CurrentLoad;

        Assert.Equal(MediaPreviewState.Unavailable, viewModel.Preview.State);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.NoPreviewStored), viewModel.Preview.StateMessage);
        Assert.Empty(viewModel.Preview.Fields);
    }

    [Fact]
    public async Task ClearingTheSelection_ReturnsThePaneToNoSelection()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);

        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog, mediaCacheStore: store);
        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single(node => node.Key == MediaRootId);
        viewModel.SelectedBrowserItem = Catalog.Single(document => document.Id == TrackId);
        await viewModel.Preview.CurrentLoad;
        Assert.Equal(MediaPreviewState.Ready, viewModel.Preview.State);

        viewModel.SelectedBrowserItem = null;
        await viewModel.Preview.CurrentLoad;

        Assert.Equal(MediaPreviewState.NoSelection, viewModel.Preview.State);
        Assert.Null(viewModel.Preview.EntryName);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.SelectItemToPreview), viewModel.Preview.StateMessage);
    }

    [Fact]
    public void PreviewPane_IsVisibleByDefaultAndToggleableFromTheViewMenu()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog);

        Assert.True(viewModel.IsPreviewPaneVisible);

        var menuItem = FindMenuItem(viewModel.ViewMenuItems, "previewpane");
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.PreviewPane), menuItem.Header);
        var checkedIcon = menuItem.Icon;

        menuItem.Command!.Execute(menuItem.CommandParameter);

        Assert.False(viewModel.IsPreviewPaneVisible);
        var toggled = FindMenuItem(viewModel.ViewMenuItems, "previewpane");
        Assert.NotEqual(checkedIcon, toggled.Icon);

        toggled.Command!.Execute(toggled.CommandParameter);

        Assert.True(viewModel.IsPreviewPaneVisible);
    }

    [Fact]
    public async Task FindDialog_SearchesCapturedMetadataThroughTheGenericFieldProvider()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);

        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog, mediaCacheStore: store);
        using var find = viewModel.CreateCatalogFindDialogViewModel();

        find.Query = "captured comment";
        await find.SearchCommand.ExecuteAsync(null);

        var match = Assert.Single(find.Results);
        Assert.Equal(TrackId, match.Document.Id);
        Assert.Equal("Comments", match.MatchedField);
    }

    [Fact]
    public void BrowserThumbnailProvider_IsAlwaysAvailableToTheList()
    {
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog);

        Assert.NotNull(viewModel.BrowserThumbnailProvider);
        Assert.IsType<MediaCacheThumbnailProvider>(viewModel.BrowserThumbnailProvider);
    }

    [Fact]
    public void Selection_ResolvesSynchronously_SoNoQueryCanOutliveTheStore()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);
        using var preview = new EntryPreviewViewModel(new MediaPreviewResolver(store));

        // Show is fire-and-forget from the UI thread. The store read has to be finished when it returns,
        // otherwise a background query can still be running after the catalog database is closed.
        preview.Show(Catalog.Single(document => document.Id == TrackId));

        Assert.NotEqual(MediaPreviewState.Loading, preview.State);
        Assert.Equal(TrackId, preview.CurrentSnapshot?.EntryId);
    }

    [Fact]
    public void EntryWithoutCacheRecords_ResolvesSynchronously()
    {
        using var store = MediaCacheStore.Create();
        using var preview = new EntryPreviewViewModel(new MediaPreviewResolver(store));

        preview.Show(Catalog.Single(document => document.Id == "file-plain-1"));

        Assert.Equal(MediaPreviewState.Unavailable, preview.State);
        Assert.NotNull(preview.CurrentSnapshot);
    }

    [Fact]
    public void DisposingViewModelAndStoreRightAfterSelection_LeavesNoQueryInFlight()
    {
        var store = MediaCacheStore.Create();
        var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(Catalog, mediaCacheStore: store);

        viewModel.SelectedBrowserItem = Catalog.Single(document => document.Id == TrackId);

        Assert.Equal(TrackId, viewModel.Preview.CurrentSnapshot?.EntryId);

        // Mirrors closing an imported catalog: the database goes away right after the selection change,
        // so anything still queued against the store would run against a disposed database.
        viewModel.Dispose();
        store.Dispose();
    }

    [Fact]
    public async Task ThumbnailRequests_ResolveSynchronouslyAgainstTheStore()
    {
        using var store = MediaCacheStore.Create();
        SaveMetadataRecord(store, TrackId);
        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver);
        var provider = new MediaCacheThumbnailProvider(resolver, images);

        var request = provider.GetThumbnailAsync(Catalog.Single(document => document.Id == TrackId), default);

        // Already completed means the store was read on this thread and nothing is queued against it.
        Assert.True(request.IsCompleted);
        Assert.Null(await request);
    }

    private static MainMenuItemViewModel FindMenuItem(
        IEnumerable<MainMenuItemViewModel> items,
        string key) =>
        items.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static void SaveMetadataRecord(IMediaCacheStore store, string entryId)
    {
        var fingerprint = $"fingerprint-{entryId}";
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, "plugin.audio", "1.0.0", "Metadata", "fake.audio", 1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = "plugin.audio",
            PluginVersion = "1.0.0",
            Capability = "Metadata",
            SchemaId = "fake.audio",
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
            SourceLength = 64,
            Metadata = [new MediaMetadataValueRecord { Key = "Comments", Type = MediaMetadataType.Text, Text = "captured comment" }],
            SchemaFields = [new MediaMetadataFieldRecord { Key = "Comments", Type = MediaMetadataType.Text }]
        }.WithRecalculatedSizes();

        store.Save(new MediaCachePayload(record));
    }
}
