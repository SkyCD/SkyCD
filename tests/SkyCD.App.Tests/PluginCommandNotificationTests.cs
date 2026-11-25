using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginCommandNotificationTests
{
    private const string Notification = "Split Catalog needs a legacy SkyCD text (.scd) catalog.";

    [Fact]
    public async Task NotificationIssuedByTheCommandOutlivesCommandCompletion()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default(),
            menuExtensionManager: new MenuExtensionManager([new NotifyingMenu()]));

        await vm.ExecutePluginMenuCommandAsync("test.notify");

        Assert.Equal(Notification, vm.StatusText);
    }

    [Fact]
    public async Task CommandWithoutNotificationCompletesWithTheDoneStatus()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default(),
            menuExtensionManager: new MenuExtensionManager([new SilentMenu()]));

        await vm.ExecutePluginMenuCommandAsync("test.silent");

        Assert.Equal(LocalizationService.Current.Get(TextKeys.Done), vm.StatusText);
    }

    private sealed class NotifyingMenu : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
            [new("test.notify", "Notify", "Tools")];

        public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) =>
            await context.HostApi!.NotifyAsync(Notification, cancellationToken);
    }

    private sealed class SilentMenu : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
            [new("test.silent", "Silent", "Tools")];

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
