using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.JetBrains;
using SkyCD.Plugin.Accounts.Microsoft365;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Accounts.Tests;

[Collection("Console capture")]
public sealed class AccountsCommandTests
{
    [Fact]
    public async Task Sync_WithImportFile_PersistsNormalizedRecords()
    {
        var directory = TempDirectory();
        var storePath = Path.Combine(directory, "accounts.json");
        var importPath = Path.Combine(directory, "jetbrains.json");
        await File.WriteAllTextAsync(importPath, """
            [
              { "product": "II", "seats": 2, "expiresUtc": "2030-01-01T00:00:00Z" },
              { "product": "Custom Tool", "seats": 1 }
            ]
            """);

        var (exitCode, output) = await RunAsync(() =>
            new AccountsSyncCommand { Providers = [new JetBrainsSyncProvider()] }.Execute("jetbrains", fromFile: importPath, storePath: storePath));

        Assert.Equal(0, exitCode);
        Assert.Contains("2 added", output, StringComparison.Ordinal);

        var data = new AccountStore(storePath).Load();
        Assert.Equal(2, data.Licenses.Count);
        Assert.Contains(data.Licenses, l => l.Product == "IntelliJ IDEA Ultimate" && l.Seats == 2);
        Assert.True(data.LastSyncedUtcByProvider.ContainsKey("jetbrains"));
    }

    [Fact]
    public async Task Sync_WithJsonOutput_EmitsStructuredSummary()
    {
        var directory = TempDirectory();
        var storePath = Path.Combine(directory, "accounts.json");
        var command = new AccountsSyncCommand { JsonOutput = true, Providers = [new JetBrainsSyncProvider()] };

        var (exitCode, output) = await RunAsync(() => command.Execute("jetbrains", storePath: storePath));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("jetbrains", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("added").GetInt32());
    }

    [Fact]
    public async Task Sync_MissingImportFile_ReturnsInvalidArguments()
    {
        var (exitCode, _) = await RunAsync(() => new AccountsSyncCommand { Providers = [new Microsoft365SyncProvider()] }.Execute("microsoft365",
            fromFile: Path.Combine(TempDirectory(), "missing.json"),
            storePath: Path.Combine(TempDirectory(), "accounts.json")));
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Sync_InvalidImportFile_ReturnsInvalidArguments()
    {
        var directory = TempDirectory();
        var importPath = Path.Combine(directory, "bad.json");
        await File.WriteAllTextAsync(importPath, """[{ "seats": 1 }]""");

        var (exitCode, _) = await RunAsync(() => new AccountsSyncCommand { Providers = [new JetBrainsSyncProvider()] }.Execute("jetbrains",
            fromFile: importPath,
            storePath: Path.Combine(directory, "accounts.json")));
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task List_EmptyStore_ReportsNoRecords()
    {
        var (licenseCode, licenseOutput) = await RunAsync(() =>
            new LicensesListCommand().Execute(storePath: TempStorePath()));
        Assert.Equal(0, licenseCode);
        Assert.Contains("No licenses recorded.", licenseOutput, StringComparison.Ordinal);

        var (subscriptionCode, subscriptionOutput) = await RunAsync(() =>
            new SubscriptionsListCommand().Execute(storePath: TempStorePath()));
        Assert.Equal(0, subscriptionCode);
        Assert.Contains("No subscriptions recorded.", subscriptionOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WithRecords_PrintsTableAndJson()
    {
        var storePath = await SeedStoreAsync();

        var (tableCode, tableOutput) = await RunAsync(() =>
            new LicensesListCommand().Execute(storePath: storePath));
        Assert.Equal(0, tableCode);
        Assert.Contains("IntelliJ IDEA Ultimate", tableOutput, StringComparison.Ordinal);

        var jsonCommand = new SubscriptionsListCommand { JsonOutput = true };
        var (jsonCode, jsonOutput) = await RunAsync(() => jsonCommand.Execute(storePath: storePath));
        Assert.Equal(0, jsonCode);
        using var document = JsonDocument.Parse(jsonOutput);
        Assert.Equal("Microsoft 365 E5", document.RootElement.GetProperty("subscriptions")[0].GetProperty("product").GetString());
        Assert.Equal("active", document.RootElement.GetProperty("subscriptions")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Export_WritesStdoutAndFile()
    {
        var storePath = await SeedStoreAsync();

        var (stdoutCode, stdoutOutput) = await RunAsync(() =>
            new InventoryExportCommand().Execute(storePath: storePath));
        Assert.Equal(0, stdoutCode);
        using (var document = JsonDocument.Parse(stdoutOutput))
        {
            Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.Single(document.RootElement.GetProperty("licenses").EnumerateArray());
        }

        var outFile = Path.Combine(TempDirectory(), "inventory.json");
        var (fileCode, fileOutput) = await RunAsync(() =>
            new InventoryExportCommand().Execute(storePath: storePath, outFile: outFile));
        Assert.Equal(0, fileCode);
        Assert.Contains("Wrote inventory to", fileOutput, StringComparison.Ordinal);
        Assert.True(File.Exists(outFile));
    }

    private static async Task<string> SeedStoreAsync()
    {
        var directory = TempDirectory();
        var storePath = Path.Combine(directory, "accounts.json");
        var importPath = Path.Combine(directory, "seed.json");
        await File.WriteAllTextAsync(importPath, """
            [{ "product": "II", "seats": 1, "expiresUtc": "2030-01-01T00:00:00Z" }]
            """);
        await new AccountsSyncCommand { Providers = [new JetBrainsSyncProvider()] }.Execute("jetbrains", fromFile: importPath, storePath: storePath);
        await File.WriteAllTextAsync(importPath, """
            [{ "product": "SPE_E5", "seats": 5, "renewsUtc": "2030-06-01T00:00:00Z" }]
            """);
        await new AccountsSyncCommand { Providers = [new Microsoft365SyncProvider()] }.Execute("microsoft365", fromFile: importPath, storePath: storePath);
        return storePath;
    }

    private static string TempStorePath() => Path.Combine(TempDirectory(), "accounts.json");

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(Func<Task<int>> invoke)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await invoke();
            return (exitCode, output.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
