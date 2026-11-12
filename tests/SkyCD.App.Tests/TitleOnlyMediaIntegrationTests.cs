using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Json;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class TitleOnlyMediaIntegrationTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-title-only-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public TitleOnlyMediaIntegrationTests()
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
            // The database file can stay locked until the process releases it.
        }
    }

    [Fact]
    public void Create_RejectsABlankName()
    {
        Assert.Throws<MediaNameRequiredException>(() => TitleOnlyMedia.Create("  ", null));
        Assert.Throws<MediaNameRequiredException>(() => TitleOnlyMedia.Create(null, "library"));
    }

    [Fact]
    public void KnownEmptyFolder_DoesNotShowAnUnknownContentsNotice()
    {
        var emptyFolder = new CatalogDocument
        {
            Id = "empty-folder",
            Name = "Empty Folder",
            Type = CatalogDocumentType.Folder,
            ChildrenCount = 0,
            ContentsState = CatalogContentsState.Known
        };

        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([emptyFolder]);
        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single(node => node.Key == emptyFolder.Id);

        Assert.Empty(viewModel.BrowserItems);
        Assert.False(viewModel.ShowContentsNotScannedNotice);
        Assert.Null(viewModel.SelectedContentsNotice);
    }

    [Fact]
    public async Task AddTitleOnlyMedia_RoundTripsThroughRestartStatusAndJsonImport()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        using var viewModel = new MainWindowViewModel(repositoryManager);
        Assert.False(viewModel.IsDirtyDocument);
        var countBefore = catalogRepository.GetAll().Count;

        var added = viewModel.AddTitleOnlyMedia("Shelf Copy", "library");

        Assert.True(viewModel.IsDirtyDocument);
        Assert.Equal(CatalogDocumentType.Media, added.Type);
        Assert.Equal(CatalogContentsState.NotScanned, added.ContentsState);
        Assert.Equal("Shelf Copy", added.Name);
        Assert.Equal("library", added.ParentId);
        Assert.Equal(0, added.ChildrenCount);
        Assert.Equal("NotScanned", added.Properties[TitleOnlyMedia.ContentsStatePropertyKey]?.ToString());
        Assert.DoesNotContain(catalogRepository.GetAll(), document => document.Name == "*.*" || document.ParentId == added.Id);
        Assert.Equal(countBefore + 1, catalogRepository.GetAll().Count);

        viewModel.SelectedTreeNode = viewModel.TreeNodes.SelectMany(Flatten).Single(node => node.Key == added.Id);
        Assert.Empty(viewModel.BrowserItems);
        Assert.True(viewModel.ShowContentsNotScannedNotice);
        Assert.Equal("Contents not scanned", viewModel.SelectedContentsNotice);
        Assert.True(viewModel.IsPropertiesEnabled);

        PropertiesDialogViewModel? propertiesDialog = null;
        viewModel.PropertiesRequested += (_, args) => propertiesDialog = args.Dialog;
        viewModel.OpenPropertiesCommand.Execute(null);
        Assert.NotNull(propertiesDialog);
        Assert.Equal("Shelf Copy", propertiesDialog.Name);
        Assert.Equal("Not scanned", propertiesDialog.InfoProperties[TitleOnlyMedia.ContentsStatePropertyKey]?.ToString());

        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single(node => node.Key == "library");
        var browserItem = viewModel.BrowserItems.Single(item => item.Id == added.Id);
        viewModel.SelectedBrowserItem = browserItem;
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { browserItem };
        viewModel.SetStatusVariants(
        [
            new StatusVariantDocument
            {
                Name = "Archived",
                IconGlyph = "check",
                ItemTypes = [CatalogDocumentType.Media]
            }
        ]);
        viewModel.ApplyBrowserItemStatusCommand.Execute("Archived");

        using var reloaded = new MainWindowViewModel(repositoryManager);
        var reloadedDocument = catalogRepository.Get(added.Id);
        Assert.NotNull(reloadedDocument);
        Assert.Equal("Shelf Copy", reloadedDocument.Name);
        Assert.Equal(CatalogDocumentType.Media, reloadedDocument.Type);
        Assert.Equal(CatalogContentsState.NotScanned, reloadedDocument.ContentsState);
        Assert.Equal("Archived", reloadedDocument.Status);
        Assert.NotEqual(CatalogDocumentType.Folder, reloadedDocument.Type);

        reloaded.SelectedTreeNode = reloaded.TreeNodes.SelectMany(Flatten).Single(node => node.Key == added.Id);
        Assert.Empty(reloaded.BrowserItems);
        Assert.Equal("Contents not scanned", reloaded.SelectedContentsNotice);

        var row = new Dictionary<string, object?>
        {
            ["id"] = reloadedDocument.Id,
            ["name"] = reloadedDocument.Name,
            ["type"] = "media",
            ["parentId"] = reloadedDocument.ParentId,
            ["contentsState"] = reloadedDocument.ContentsState.ToString(),
            ["childrenCount"] = reloadedDocument.ChildrenCount,
            ["size"] = reloadedDocument.Size,
            ["status"] = reloadedDocument.Status,
            ["properties"] = reloadedDocument.Properties.ToDictionary(item => item.Key, item => item.Value)
        };

        await using var stream = new MemoryStream();
        var write = await new JsonCatalogPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-json",
            FileName = "title-only.json",
            Target = stream,
            Payload = new List<Dictionary<string, object?>> { row }
        });
        Assert.True(write.Success, write.Error);

        stream.Position = 0;
        var read = await new JsonCatalogPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-json",
            FileName = "title-only.json",
            Source = stream
        });
        Assert.True(read.Success, read.Error);

        var imported = CatalogImportTests.Extract(read.Payload!);
        var importedMedia = Assert.Single(imported);
        Assert.Equal("Shelf Copy", importedMedia.Name);
        Assert.Equal(CatalogDocumentType.Media, importedMedia.Type);
        Assert.Equal(CatalogContentsState.NotScanned, importedMedia.ContentsState);
        Assert.Equal("library", importedMedia.ParentId);
        Assert.Equal("NotScanned", importedMedia.Properties[TitleOnlyMedia.ContentsStatePropertyKey]?.ToString());
        Assert.DoesNotContain(imported, document => document.Name == "*.*");
    }

    [Fact]
    public void DecliningTheDialog_LeavesTheCatalogUnchanged()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();
        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var namesBefore = catalogRepository.GetAll().Select(document => document.Id).OrderBy(id => id).ToArray();
        var dialog = new AddToListDialogViewModel { SourceMode = AddToListSourceMode.TitleOnly, MediaName = "Not Added" };
        Assert.True(dialog.CanConfirm);
        Assert.False(dialog.DialogAccepted);

        Assert.False(viewModel.IsDirtyDocument);
        Assert.Equal(namesBefore, catalogRepository.GetAll().Select(document => document.Id).OrderBy(id => id).ToArray());
        Assert.DoesNotContain(catalogRepository.GetAll(), document => document.Name == "Not Added");
    }

    private static IEnumerable<BrowserTreeNode> Flatten(BrowserTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
