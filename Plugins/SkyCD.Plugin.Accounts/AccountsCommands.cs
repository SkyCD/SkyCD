using System;
using System.IO;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;

namespace SkyCD.Plugin.Accounts;

/// <summary>
/// Plugin-defined <c>sync</c> commands: <c>sync jetbrains</c> and <c>sync microsoft365</c>.
/// </summary>
[Command("sync")]
public sealed class AccountsSyncCommand : ICliPluginCapability
{
    [Subcommand] public JetBrainsSyncCommand JetBrains { get; } = new();
    [Subcommand] public Microsoft365SyncCommand Microsoft365 { get; } = new();
}

/// <summary>
/// Syncs JetBrains license records into the local account store.
/// </summary>
[Command("jetbrains")]
public sealed class JetBrainsSyncCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute(
        [Option("from")] string? fromFile = null,
        [Option("store")] string? storePath = null)
    {
        return AccountsCliOperations.SyncAsync(
            new JetBrainsSyncProvider(), fromFile, storePath, JsonOutput, Console.Out, Console.Error);
    }
}

/// <summary>
/// Syncs Microsoft 365 subscription records into the local account store.
/// </summary>
[Command("microsoft365")]
public sealed class Microsoft365SyncCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute(
        [Option("from")] string? fromFile = null,
        [Option("store")] string? storePath = null)
    {
        return AccountsCliOperations.SyncAsync(
            new Microsoft365SyncProvider(), fromFile, storePath, JsonOutput, Console.Out, Console.Error);
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
