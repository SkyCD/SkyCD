using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginFailureIntegrationTests
{
    [Fact]
    public async Task GuardedCommandPublishesSafeReportEvenWhenAnotherObserverFails()
    {
        PluginFailure? captured = null;
        void Capture(PluginFailure failure) => captured = failure;
        void BrokenObserver(PluginFailure _) => throw new InvalidOperationException();
        PluginFailure.Reported += BrokenObserver;
        PluginFailure.Reported += Capture;
        try
        {
            var manager = new MenuExtensionManager([new FailingMenu()]);
            var result = await manager.ExecuteAsync("fail", new MenuCommandContext(), TimeSpan.FromSeconds(5));
            Assert.False(result.Success);
            Assert.DoesNotContain("private", result.Error!);
            Assert.NotNull(captured);
            Assert.Equal("menu", captured.Operation);
            Assert.Equal(64, captured.Fingerprint.Length);
        }
        finally
        {
            PluginFailure.Reported -= BrokenObserver;
            PluginFailure.Reported -= Capture;
        }
    }

    [Fact]
    public async Task CallerCancellationDoesNotProduceFailureReport()
    {
        var reported = false;
        void Capture(PluginFailure _) => reported = true;
        PluginFailure.Reported += Capture;
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var manager = new MenuExtensionManager([new FailingMenu()]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ExecuteAsync(
                "fail", new MenuCommandContext(), TimeSpan.FromSeconds(5), cts.Token));
            Assert.False(reported);
        }
        finally { PluginFailure.Reported -= Capture; }
    }

    private sealed class FailingMenu : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() => [new("fail", "Fail", "tools")];

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException(new InvalidOperationException("private token=secret C:\\Users\\private"));
        }
    }
}
