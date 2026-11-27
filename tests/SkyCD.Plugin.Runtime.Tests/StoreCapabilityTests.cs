using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Ownership;
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
        Assert.Same(store, container.Resolve<IOwnershipProvider>(serviceKey: typeof(IOwnershipProvider)));

        using var cancellation = new CancellationTokenSource();
        await container.ResolveMany<IOwnershipProvider>().Distinct().Single().SyncAsync(cancellation.Token);
        var connection = await container.ResolveMany<IAccountConnector>().Distinct().Single().ConnectAsync(cancellation.Token);
        Assert.True(connection.Success);
        Assert.Equal("account-341", connection.AccountId);

        Assert.Equal(new[] { "sync", "connect" }, store.Calls);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SyncAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ConnectAsync(cancellation.Token));
    }
}

public sealed class StoreCapability : IOwnershipProvider, IAccountConnector
{
    public List<string> Calls { get; } = [];
    public List<CancellationToken> Tokens { get; } = [];

    public Task SyncAsync(CancellationToken cancellationToken = default) => RecordAsync("sync", cancellationToken);

    public async Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await RecordAsync("connect", cancellationToken);
        return new AccountConnectionResult { Success = true, AccountId = "account-341" };
    }

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
