using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.Plugin.Host.Menu;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class SplitMenuPluginTests
{
    [Fact]
    public void Contributions_RegisterTheSplitCommandUnderTools()
    {
        var contributions = new SplitMenuPlugin().GetMenuContributions();

        var contribution = Assert.Single(contributions);
        Assert.Equal("skycd.split", contribution.CommandId);
        Assert.Equal("Tools", contribution.Location);
        Assert.Equal("Split Catalog...", contribution.Title);
    }

    [Fact]
    public async Task UnknownCommandIsIgnored()
    {
        var plugin = new SplitMenuPlugin();
        var host = new RecordingHostApi();

        await plugin.ExecuteMenuCommandAsync("other.command", new MenuCommandContext { HostApi = host });

        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task WithoutAnOpenCatalogItNotifiesInsteadOfOpeningTheDialog()
    {
        var plugin = new SplitMenuPlugin();
        plugin.InitializeCatalogApi(new FakeCatalogApi(null));
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        var host = new RecordingHostApi();

        await plugin.ExecuteMenuCommandAsync(SplitMenuPlugin.CommandId, new MenuCommandContext { HostApi = host });

        Assert.Empty(dialogs.Requests);
        Assert.Contains("legacy SkyCD text", Assert.Single(host.Messages));
    }

    [Fact]
    public async Task WithoutAHostDialogItNotifies()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var plugin = new SplitMenuPlugin();
        plugin.InitializeCatalogApi(new FakeCatalogApi(source));
        var host = new RecordingHostApi();

        await plugin.ExecuteMenuCommandAsync(SplitMenuPlugin.CommandId, new MenuCommandContext { HostApi = host });

        Assert.Contains("host dialog", Assert.Single(host.Messages));
    }

    [Fact]
    public async Task ABinaryLegacyCatalogIsRejectedBeforeTheDialogOpens()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateFile("binary.scd", "# SkyCD File\r\n\x1a\x04junk");
        var plugin = new SplitMenuPlugin();
        plugin.InitializeCatalogApi(new FakeCatalogApi(source));
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        var host = new RecordingHostApi();

        await plugin.ExecuteMenuCommandAsync(SplitMenuPlugin.CommandId, new MenuCommandContext { HostApi = host });

        Assert.Empty(dialogs.Requests);
        Assert.Contains("not a legacy SkyCD text catalog", Assert.Single(host.Messages));
    }

    [Fact]
    public async Task ALegacyTextCatalogOpensTheSplitDialog()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var plugin = new SplitMenuPlugin();
        plugin.InitializeCatalogApi(new FakeCatalogApi(source));
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        var host = new RecordingHostApi();

        await plugin.ExecuteMenuCommandAsync(SplitMenuPlugin.CommandId, new MenuCommandContext { HostApi = host });

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Split Catalog", request.Title);
        Assert.True(request.Width >= 560);
        Assert.True(request.Height >= 420);
        Assert.Empty(host.Messages);
    }

    [Fact]
    public async Task TheHostRoutesTheCommandToTheSplitCapability()
    {
        var plugin = new SplitMenuPlugin();
        var dialogs = new RecordingDialogApi();
        plugin.InitializeDialogApi(dialogs);
        using var workspace = new TestWorkspace();
        plugin.InitializeCatalogApi(new FakeCatalogApi(StageCatalog(workspace)));
        var manager = new MenuExtensionManager([plugin]);
        var host = new RecordingHostApi();

        Assert.Contains("skycd.split", manager.GetMenuContributions("Tools")
            .Select(contribution => contribution.CommandId));
        var result = await manager.ExecuteAsync("skycd.split", new MenuCommandContext { HostApi = host },
            TimeSpan.FromSeconds(30));

        Assert.True(result.Success, result.Error);
        Assert.Single(dialogs.Requests);
    }

    private static string StageCatalog(TestWorkspace workspace) => workspace.CreateFile("source/gamez.scd",
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Legacy", "catalog-sample.scd")));

    private sealed class FakeCatalogApi(string? path) : ICatalogApi
    {
        public string? OpenedCatalogPath { get; } = path;

        public Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogEntry>>([]);

        public Task<CatalogEntry?> GetEntryAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogEntry?>(null);
    }

    private sealed class RecordingHostApi : IHostCommandApi
    {
        public List<string> Messages { get; } = [];

        public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDialogApi : IHostDialogApi
    {
        public List<HostWindowRequest> Requests { get; } = [];

        public Task ShowAsync(HostWindowRequest request)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
