using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class MenuExtensionManagerTests
{
    [Fact]
    public void LegacyFourArgumentOverload_ExistsForPluginsBuiltAgainstOlderContract()
    {
        var overload = typeof(MenuContribution).GetConstructor(
            [typeof(string), typeof(string), typeof(string), typeof(int)]);

        Assert.NotNull(overload);
        var contribution = (MenuContribution)overload.Invoke(["demo.legacy", "Legacy", "Tools", 10]);
        Assert.Equal("demo.legacy", contribution.CommandId);
        Assert.Equal(10, contribution.Order);
        Assert.False(contribution.RequiresLocalFile);
    }

    [Fact]
    public void Constructor_SkipsCapabilityWhoseContributionsThrow()
    {
        PluginFailure? captured = null;
        void Capture(PluginFailure failure) => captured = failure;
        PluginFailure.Reported += Capture;
        try
        {
            var manager = new MenuExtensionManager([new ThrowingMenu(), new HealthyMenu()]);

            var contributions = manager.GetMenuContributions("Tools");

            var single = Assert.Single(contributions);
            Assert.Equal("healthy", single.CommandId);
            Assert.NotNull(captured);
            Assert.Equal("menu", captured.Operation);
        }
        finally
        {
            PluginFailure.Reported -= Capture;
        }
    }

    private sealed class ThrowingMenu : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
            throw new MissingMethodException("Simulates a plugin built against an older SDK contract.");

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class HealthyMenu : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
            [new("healthy", "Healthy", "Tools", 5)];

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
