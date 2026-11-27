using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Feature.Split.Localization;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public sealed class SplitPluginTests
{
    static SplitPluginTests() => SplitTestLocalization.EnsureRegistered();

    [Fact]
    public void ContributesOneFileMenuEntryThatNeedsAWritableFormat()
    {
        var contribution = Assert.Single(new SplitPlugin().GetMenuContributions());

        Assert.Equal(SplitPlugin.SplitCommandId, contribution.CommandId);
        Assert.Equal(LocalizationService.Current.Get(SplitKeys.MenuTitle), contribution.Title);
        Assert.NotEqual(SplitKeys.MenuTitle, contribution.Title);
        Assert.Equal("File", contribution.Location);
        Assert.Equal(140, contribution.Order);
        Assert.True(contribution.RequiresWritableFormat);
        Assert.False(contribution.RequiresLocalFile);
    }

    [Fact]
    public void TranslationsCoverBothLanguages()
    {
        var translations = new SplitPlugin().GetTranslations();

        Assert.Contains("en", translations.Keys);
        Assert.Contains("lt", translations.Keys);
    }

    [Fact]
    public async Task UnknownCommandsAreIgnored()
    {
        var host = new RecordingHostCommandApi();
        var plugin = CreatePlugin(SampleCatalog());

        await plugin.ExecuteMenuCommandAsync("skycd.split.other", Context(host));

        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task WithoutAnOpenCatalogTheCommandIsReported()
    {
        var catalog = SampleCatalog();
        catalog.OpenedCatalogPath = null;
        var host = new RecordingHostCommandApi();
        var plugin = CreatePlugin(catalog);

        await plugin.ExecuteMenuCommandAsync(SplitPlugin.SplitCommandId, Context(host));

        Assert.Equal(LocalizationService.Current.Get(SplitKeys.NoOpenCatalog), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task WithoutTheHostSaveSurfaceTheCommandIsReported()
    {
        var catalog = SampleCatalog();
        var host = new RecordingHostCommandApi();
        var plugin = new SplitPlugin();
        plugin.InitializeCatalogApi(catalog.Api);

        await plugin.ExecuteMenuCommandAsync(SplitPlugin.SplitCommandId, Context(host));

        Assert.Equal(LocalizationService.Current.Get(SplitKeys.HostUnavailable), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task AnEmptyCatalogIsReported()
    {
        var catalog = new TestCatalog([]);
        var host = new RecordingHostCommandApi();
        var plugin = CreatePlugin(catalog);

        await plugin.ExecuteMenuCommandAsync(SplitPlugin.SplitCommandId, Context(host));

        Assert.Equal(LocalizationService.Current.Get(SplitKeys.NoEntries), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task WithoutAWritableFormatTheCommandIsReported()
    {
        var host = new RecordingHostCommandApi();
        var plugin = new SplitPlugin();
        plugin.InitializeCatalogApi(SampleCatalog().Api);
        plugin.InitializeCatalogSaveHost(new NoFormatsSaveHost());
        plugin.InitializeDialogApi(new RecordingDialogApi());

        await plugin.ExecuteMenuCommandAsync(SplitPlugin.SplitCommandId, Context(host));

        Assert.Equal(LocalizationService.Current.Get(SplitKeys.NoWritableFormat), Assert.Single(host.Messages));
    }

    [Fact]
    public async Task TheCommandShowsTheSplitFormInAHostOwnedWindow()
    {
        var catalog = SampleCatalog();
        var host = new RecordingHostCommandApi();
        var dialogs = new RecordingDialogApi();
        var plugin = CreatePlugin(catalog);
        plugin.InitializeDialogApi(dialogs);

        await plugin.ExecuteMenuCommandAsync(SplitPlugin.SplitCommandId, Context(host));

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(LocalizationService.Current.Get(SplitKeys.DialogTitle), request.Title);
        Assert.Equal(620, request.Width);
        Assert.Equal(560, request.Height);
        Assert.Empty(host.Messages);
    }

    private static SplitPlugin CreatePlugin(TestCatalog catalog)
    {
        var plugin = new SplitPlugin();
        plugin.InitializeCatalogApi(catalog.Api);
        plugin.InitializeCatalogSaveHost(catalog.SaveHost);
        plugin.InitializeDialogApi(new RecordingDialogApi());
        return plugin;
    }

    private static TestCatalog SampleCatalog() =>
        new(Enumerable.Range(1, 3).Select(index => new CatalogDocument
        {
            Id = $"file-{index}",
            Name = $"File{index:00}.dat",
            ParentId = null,
            Type = CatalogDocumentType.File,
            Size = index * 100,
            ChildrenCount = 0
        }).ToArray());

    private static MenuCommandContext Context(IHostCommandApi host) => new() { HostApi = host };

    private sealed class NoFormatsSaveHost : ICatalogSaveHostApi
    {
        public IReadOnlyList<FileFormatDescriptor> GetRewritableFormats() => [];

        public string ResolveRewritableFormatId(string? path) => string.Empty;

        public IReadOnlyList<FileFormatDataLossItem> CollectCatalogScopeItems() => [];

        public IReadOnlyList<FileFormatDataLossItem> AssessDataLoss(string formatId, string fileName,
            IReadOnlyList<CatalogEntry> entries, IReadOnlyList<FileFormatDataLossItem> catalogScopeItems) => [];

        public Task WriteAsync(string formatId, string fileName, System.IO.Stream target,
            IReadOnlyList<CatalogEntry> entries, IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteFileAsync(string formatId, string path, IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
