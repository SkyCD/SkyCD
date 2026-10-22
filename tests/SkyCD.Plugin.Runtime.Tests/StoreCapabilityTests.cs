using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Abstractions.Capabilities.Actions;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Ownership;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class StoreCapabilityTests
{
    [Fact]
    public async Task DiscoveredCapabilities_RegisterSameInstanceAndExecuteThroughContracts()
    {
        // The implementation lives in a different assembly from the SDK contracts.
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());
        var store = Assert.Single(plugin.Capabilities.OfType<StoreCapability>());
        Assert.Contains(plugin.Capabilities, capability => capability is IFileFormatPluginCapability);
        using var container = new Container();
        plugin.RegisterPluginServices(container);

        Assert.Same(store, container.ResolveMany<IOwnershipProvider>().Distinct().Single());
        Assert.Same(store, container.ResolveMany<IAccountConnector>().Distinct().Single());
        Assert.Same(store, container.ResolveMany<IActionProvider>().Distinct().Single());
        Assert.Same(store, container.Resolve<IOwnershipProvider>(serviceKey: typeof(IOwnershipProvider)));

        using var cancellation = new CancellationTokenSource();
        await container.ResolveMany<IOwnershipProvider>().Distinct().Single().SyncAsync(cancellation.Token);
        var connection = await container.ResolveMany<IAccountConnector>().Distinct().Single().ConnectAsync(cancellation.Token);
        Assert.True(connection.Success);
        Assert.Equal("account-341", connection.AccountId);
        var item = new Item { Id = "game-341" };
        var actions = container.ResolveMany<IActionProvider>().Distinct().Single().GetActions(item).ToArray();
        Assert.Equal(new[] { "install", "launch" }, actions.Select(action => action.Id));
        foreach (var action in actions)
        {
            await action.ExecuteAsync(cancellation.Token);
        }

        Assert.Equal(new[] { "sync", "connect", "install:game-341", "launch:game-341" }, store.Calls);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SyncAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ConnectAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actions[0].ExecuteAsync(cancellation.Token));
    }
}

public sealed class StoreCapability : IOwnershipProvider, IAccountConnector, IActionProvider
{
    public List<string> Calls { get; } = [];
    public List<CancellationToken> Tokens { get; } = [];

    public Task SyncAsync(CancellationToken cancellationToken = default) => RecordAsync("sync", cancellationToken);

    public async Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await RecordAsync("connect", cancellationToken);
        return new AccountConnectionResult { Success = true, AccountId = "account-341" };
    }

    public IEnumerable<SkyAction> GetActions(Item item) =>
    [
        new("install", "Install", token => RecordAsync($"install:{item.Id}", token)),
        new("launch", "Launch", token => RecordAsync($"launch:{item.Id}", token))
    ];

    private Task RecordAsync(string operation, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled(token);
        }

        Calls.Add(operation);
        Tokens.Add(token);
        return Task.CompletedTask;
    }
}
