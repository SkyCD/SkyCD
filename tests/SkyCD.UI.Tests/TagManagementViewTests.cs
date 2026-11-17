using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Tags;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class TagManagementViewTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-tag-ui-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");
    private DryIoc.Container? container;

    public TagManagementViewTests()
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
        TagDocumentRepository Tags,
        CatalogTagService Service,
        MainWindowViewModel ViewModel,
        string SciFiTagId,
        string DramaTagId);

    private Fixture CreateFixture()
    {
        var created = new DryIoc.Container();
        container?.Dispose();
        container = created;
        new CouchbaseServiceRegistrator().RegisterServices(created);
        var repositories = created.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        var tags = (TagDocumentRepository)repositories.For<TagDocument>();

        Save(catalog, new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "scifi", Name = "Sci-Fi", ParentId = "movies", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "interstellar", Name = "Interstellar.mkv", ParentId = "scifi", Type = CatalogDocumentType.File, Size = 4096, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "odyssey", Name = "Odyssey.mp4", ParentId = "scifi", Type = CatalogDocumentType.File, Size = 2048, Properties = new PropertiesCollection() });
        Save(catalog, new CatalogDocument { Id = "drama", Name = "Drama", ParentId = "movies", Type = CatalogDocumentType.Folder, Properties = new PropertiesCollection() });

        var service = new CatalogTagService(catalog, tags);
        var sciFi = service.CreateTag("SciFi");
        var drama = service.CreateTag("Drama");
        service.AssignTags(["interstellar"], [sciFi.Id]);
        service.AssignTags(["drama"], [drama.Id]);

        var viewModel = new MainWindowViewModel(repositories);
        return new Fixture(repositories, catalog, tags, service, viewModel, sciFi.Id, drama.Id);
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

    private static void SelectInFolder(MainWindowViewModel viewModel, string nodeKey, params string[] names)
    {
        viewModel.SelectedTreeNode = FindNode(viewModel, nodeKey);
        Dispatcher.UIThread.RunJobs();
        var selected = viewModel.BrowserItems
            .Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        viewModel.SelectedBrowserItems = new List<CatalogDocument>(selected);
        Dispatcher.UIThread.RunJobs();
    }

    private static MainMenuItemViewModel TagsParent(MainWindowViewModel viewModel) =>
        viewModel.BrowserContextMenuItems.Single(item => item.Key == "edit_tags");

    private static MainMenuItemViewModel TagItem(MainWindowViewModel viewModel, string tagId) =>
        TagsParent(viewModel).Items
            .OfType<MainMenuItemViewModel>()
            .Single(item => string.Equals(item.CommandParameter as string, tagId, StringComparison.Ordinal));

    [AvaloniaFact]
    public void MixedSelection_ShowsTristateTagIcons()
    {
        var fixture = CreateFixture();

        SelectInFolder(fixture.ViewModel, "scifi", "Interstellar.mkv", "Odyssey.mp4");
        Assert.Equal("◐", TagItem(fixture.ViewModel, fixture.SciFiTagId).Icon);
        Assert.Equal("☐", TagItem(fixture.ViewModel, fixture.DramaTagId).Icon);

        SelectInFolder(fixture.ViewModel, "scifi", "Interstellar.mkv");
        Assert.Equal("☑", TagItem(fixture.ViewModel, fixture.SciFiTagId).Icon);
        Assert.Equal("☐", TagItem(fixture.ViewModel, fixture.DramaTagId).Icon);
    }

    [AvaloniaFact]
    public async Task ToggleTag_AddOnlyToMissingWithoutTouchingOthers_ThenUndoes()
    {
        var fixture = CreateFixture();

        SelectInFolder(fixture.ViewModel, "scifi", "Interstellar.mkv", "Odyssey.mp4");
        fixture.ViewModel.ToggleTagForEntriesCommand.Execute(fixture.SciFiTagId);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([fixture.SciFiTagId], fixture.Service.GetEntryTagIds("interstellar"));
        Assert.Equal([fixture.SciFiTagId], fixture.Service.GetEntryTagIds("odyssey"));
        Assert.Equal("☑", TagItem(fixture.ViewModel, fixture.SciFiTagId).Icon);

        await fixture.ViewModel.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([fixture.SciFiTagId], fixture.Service.GetEntryTagIds("interstellar"));
        Assert.Empty(fixture.Service.GetEntryTagIds("odyssey"));
    }

    [AvaloniaFact]
    public void ToggleTag_RemovesEverywhereWhenAllAssigned()
    {
        var fixture = CreateFixture();

        SelectInFolder(fixture.ViewModel, "scifi", "Interstellar.mkv");
        Assert.Equal("☑", TagItem(fixture.ViewModel, fixture.SciFiTagId).Icon);

        fixture.ViewModel.ToggleTagForEntriesCommand.Execute(fixture.SciFiTagId);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(fixture.Service.GetEntryTagIds("interstellar"));
        Assert.Equal("☐", TagItem(fixture.ViewModel, fixture.SciFiTagId).Icon);
    }

    [AvaloniaFact]
    public void ManagerDialog_CreateRenameMergeDelete_Persist()
    {
        var fixture = CreateFixture();
        var undo = new CatalogUndoManager(fixture.Catalog);
        var dialog = new TagsDialogViewModel(
            fixture.Service, undo, fixture.Catalog, fixture.Tags);
        Assert.True(dialog.TagsAvailable);
        Assert.Equal(2, dialog.Tags.Count);

        dialog.NewTagName = "Noir";
        dialog.CreateTagCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var noir = Assert.Single(dialog.Tags, tag => tag.Name == "Noir");

        dialog.SelectedTag = noir;
        Dispatcher.UIThread.RunJobs();
        dialog.RenameTo = "NeoNoir";
        dialog.RenameTagCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(dialog.Tags, tag => tag.Name == "NeoNoir");

        dialog.MergeTarget = dialog.Tags.Single(tag => tag.Name == "SciFi");
        dialog.MergeTagsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(dialog.Tags, tag => tag.Name == "NeoNoir");

        dialog.SelectedTag = dialog.Tags.Single(tag => tag.Name == "Drama");
        Dispatcher.UIThread.RunJobs();
        dialog.DeleteTagCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(dialog.Tags, tag => tag.Name == "Drama");
        Assert.Empty(fixture.Service.GetEntryTagIds("drama"));

        using var second = new MainWindowViewModel(fixture.Repositories);
        Assert.Contains(fixture.Service.GetTags(), tag => tag.Name == "SciFi");
        _ = second;
    }

    [AvaloniaFact]
    public void ManagerDialog_ShowItemsUsesSearchContractAndNavigates()
    {
        var fixture = CreateFixture();
        CatalogDocument? navigated = null;
        var dialog = new TagsDialogViewModel(
            fixture.Service,
            new CatalogUndoManager(fixture.Catalog),
            fixture.Catalog,
            fixture.Tags,
            navigate: document => navigated = document);

        dialog.SelectedTag = dialog.Tags.Single(tag => tag.Name == "SciFi");
        Dispatcher.UIThread.RunJobs();
        dialog.ShowItemsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var match = Assert.Single(dialog.FilterResults);
        Assert.Equal("interstellar", match.Document.Id);

        dialog.SelectedResult = match;
        Dispatcher.UIThread.RunJobs();
        dialog.NavigateToSelectedCommand.Execute(null);
        Assert.Equal("interstellar", navigated?.Id);
    }

    [AvaloniaFact]
    public void PropertiesTab_AssignsWithoutTouchingOnCancel()
    {
        var fixture = CreateFixture();

        var dialog = new PropertiesDialogViewModel(
            "item:library:Interstellar.mkv",
            "Interstellar.mkv",
            string.Empty,
            string.Empty,
            new PropertiesCollection(),
            new PropertyValueLocalizer());
        dialog.InitializeTags(
            fixture.Service.GetTags().Select(tag => (tag.Id, tag.Name)),
            fixture.Service.GetEntryTagIds("interstellar"));

        Assert.True(dialog.HasTagsTab);
        var dramaRow = dialog.TagAssignments.Single(item => item.TagId == fixture.DramaTagId);
        Assert.False(dramaRow.IsAssigned);
        dramaRow.IsAssigned = true;

        Assert.Equal(
            [fixture.DramaTagId, fixture.SciFiTagId],
            dialog.GetAssignedTagIds().OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal([fixture.SciFiTagId], fixture.Service.GetEntryTagIds("interstellar"));
    }

    [AvaloniaFact]
    public void OpenTagsDialogCommand_RaisesRequestWithPopulatedDialog()
    {
        var fixture = CreateFixture();
        TagsDialogViewModel? dialog = null;
        fixture.ViewModel.TagsRequested += (_, args) => dialog = args.Dialog;

        SelectInFolder(fixture.ViewModel, "scifi", "Interstellar.mkv");
        fixture.ViewModel.OpenTagsDialogCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog);
        Assert.Equal(2, dialog.Tags.Count);
    }

    [AvaloniaFact]
    public void KeyboardEnter_OnTagMenuItem_TogglesAssignment()
    {
        var fixture = CreateFixture();
        SelectInFolder(fixture.ViewModel, "movies", "Drama");

        var menuItem = new MenuItem
        {
            Header = "Drama",
            Command = fixture.ViewModel.ToggleTagForEntriesCommand,
            CommandParameter = fixture.DramaTagId,
            IsEnabled = true
        };
        var menu = new Menu();
        menu.Items.Add(menuItem);
        var window = new Window { Width = 200, Height = 200, Content = menu };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(menuItem.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(menuItem, window.FocusManager?.GetFocusedElement());
        Assert.Equal([fixture.DramaTagId], fixture.Service.GetEntryTagIds("drama"));

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(fixture.Service.GetEntryTagIds("drama"));

        window.Close();
    }
}

