using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandDotNet;
using CommandDotNet.Builders;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.JetBrains;
using SkyCD.Plugin.Accounts.Microsoft365;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Accounts.Core.Models;

namespace SkyCD.Plugin.Accounts.Core.Tests;

[Collection("Console capture")]
public sealed class AccountPluginDiscoveryTests
{
    [Fact]
    public void ProvidersAreDiscoveredAsIndependentPluginsAndRespectEnabledState()
    {
        var factory = new DiscoveredPluginFactory();
        var jetBrains = factory.BuildFromAssembly(typeof(JetBrainsSyncProvider).Assembly);
        var microsoft = factory.BuildFromAssembly(typeof(Microsoft365SyncProvider).Assembly);
        var accounts = factory.BuildFromAssembly(typeof(AccountsSyncCommand).Assembly);
        Assert.NotEqual(jetBrains.Id, microsoft.Id);
        Assert.Single(jetBrains.Capabilities.OfType<IAccountSyncProvider>());
        Assert.Single(microsoft.Capabilities.OfType<IAccountSyncProvider>());
        Assert.Empty(accounts.Capabilities.OfType<IAccountSyncProvider>());
        var disabled = jetBrains.ToDocument();
        disabled.IsEnabled = false;
        var enabled = new DiscoveredPluginCollection();
        enabled.Import([jetBrains, microsoft, accounts], [disabled]);
        var provider = Assert.Single(enabled.SelectMany(plugin => plugin.Capabilities).OfType<IAccountSyncProvider>());
        Assert.Equal("microsoft365", provider.ProviderId);
    }

    [Fact]
    public async Task ThirdPartyProviderDispatchesThroughGenericCommandWithJsonOutput()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ThirdPartyAccountProvider).Assembly);
        var command = new AccountsSyncCommand
        {
            JsonOutput = true,
            Providers = plugin.Capabilities.OfType<IAccountSyncProvider>().ToArray()
        };
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var import = Path.Combine(directory, "import.json");
            await File.WriteAllTextAsync(import, """[{"product":"Independent Tool"}]""");
            var store = Path.Combine(directory, "accounts.json");
            var exitCode = await new AppRunner<AccountsSyncCommand>()
                .UseDependencyResolver(new AccountCommandResolver(command))
                .RunAsync(["THIRDPARTY", "--from", import, "--store", store]);
            Assert.Equal(0, exitCode);
            Assert.Contains("\"provider\": \"thirdparty\"", output.ToString(), StringComparison.Ordinal);
            Assert.Equal("Independent Tool", Assert.Single(new AccountStore(store).Load().Licenses).Product);
        }
        finally
        {
            Console.SetOut(previousOut);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrAmbiguousProviderDoesNotModifyStore(bool duplicate)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "accounts.json");
        await File.WriteAllTextAsync(path, "existing data");
        var command = new AccountsSyncCommand
        {
            Providers = duplicate ? [new ThirdPartyAccountProvider(), new ThirdPartyAccountProvider()] : []
        };
        var previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(2, await command.Execute("thirdparty", storePath: path));
            Assert.Equal("existing data", await File.ReadAllTextAsync(path));
            Assert.Contains(duplicate ? "Multiple" : "not installed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previousError);
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class ThirdPartyAccountProvider : IAccountSyncProvider
{
    public string ProviderId => "thirdparty";
    public string DisplayName => "Third party";

    public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows) =>
        rows.Select(row => new LicenseRecord
        {
            Id = "thirdparty:" + row.Product,
            Provider = ProviderId,
            Product = row.Product!
        }).ToArray();

    public IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows) => [];
}

internal sealed class AccountCommandResolver(AccountsSyncCommand command) : IDependencyResolver
{
    public object Resolve(Type type) => command;
    public bool TryResolve(Type type, out object instance)
    {
        instance = command;
        return type == typeof(AccountsSyncCommand);
    }
}
