using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.Import;

namespace SkyCD.Plugin.Accounts;

[Command("sync")]
public sealed class AccountsSyncCommand : ICliPluginCapability, ISupportsJsonOutput, IAccountSyncProviderConsumer
{
    public bool JsonOutput { get; set; }

    public IReadOnlyList<IAccountSyncProvider> Providers { get; set; } = [];

    [DefaultCommand]
    public async Task<int> Execute(
        string providerId,
        [Option("from")] string? fromFile = null,
        [Option("store")] string? storePath = null)
    {
        var matches = Providers.Where(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            await Console.Error.WriteLineAsync(matches.Length == 0
                ? $"Account provider '{providerId}' is not installed or enabled. Available providers: {string.Join(", ", Providers.Select(provider => provider.ProviderId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}."
                : $"Multiple installed account plugins declare provider ID '{providerId}'. Disable the conflicting plugin.");
            return 2;
        }

        return await AccountsCliOperations.SyncAsync(
            matches[0], fromFile, storePath, JsonOutput, Console.Out, Console.Error);
    }
}

/// <summary>
/// Plugin-defined <c>list</c> commands: <c>list licenses</c> and <c>list subscriptions</c>.
/// </summary>
[Command("list")]
public sealed class AccountsListCommand : ICliPluginCapability
{
    [Subcommand] public LicensesListCommand Licenses { get; } = new();
    [Subcommand] public SubscriptionsListCommand Subscriptions { get; } = new();
}

/// <summary>
/// Lists license records in human or JSON form.
/// </summary>
[Command("licenses")]
public sealed class LicensesListCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute([Option("store")] string? storePath = null)
    {
        return AccountsCliOperations.ListAsync("licenses", storePath, JsonOutput, Console.Out, Console.Error);
    }
}

/// <summary>
/// Lists subscription records in human or JSON form.
/// </summary>
[Command("subscriptions")]
public sealed class SubscriptionsListCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute([Option("store")] string? storePath = null)
    {
        return AccountsCliOperations.ListAsync("subscriptions", storePath, JsonOutput, Console.Out, Console.Error);
    }
}

/// <summary>
/// Plugin-defined <c>export</c> commands: <c>export inventory</c>.
/// </summary>
[Command("export")]
public sealed class AccountsExportCommand : ICliPluginCapability
{
    [Subcommand] public InventoryExportCommand Inventory { get; } = new();
}

/// <summary>
/// Exports the full ownership inventory as JSON to standard output or a file.
/// </summary>
[Command("inventory")]
public sealed class InventoryExportCommand : ICliPluginCapability
{
    [DefaultCommand]
    public Task<int> Execute(
        [Option("store")] string? storePath = null,
        [Option("out")] string? outFile = null)
    {
        return AccountsCliOperations.ExportInventoryAsync(storePath, outFile, Console.Out, Console.Error);
    }
}
