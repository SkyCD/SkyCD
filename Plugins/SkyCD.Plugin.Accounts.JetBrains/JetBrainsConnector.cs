using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Abstractions.Capabilities.Actions;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.Ownership;
using SkyCD.Plugin.Abstractions.OAuth;
using SkyCD.Plugin.Accounts.JetBrains.Exceptions;
using SkyCD.Plugin.Accounts.JetBrains.Models;
using SkyCD.Plugin.Accounts.JetBrains.Parsing;
using SkyCD.Plugin.Accounts.JetBrains.Sources;

namespace SkyCD.Plugin.Accounts.JetBrains;

/// <summary>
/// OAuth-first JetBrains connector: account sign-in, owned license sync, installed IDE discovery,
/// ownership status and launch actions. Commands are exposed as <c>skycd jetbrains ...</c>.
/// </summary>
[Command("jetbrains")]
public sealed class JetBrainsConnector : IOAuthClient, IAccountConnector, IOwnershipProvider, IActionProvider,
    ICliPluginCapability, ISupportsJsonOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly JetBrainsSettings settings;
    private readonly HttpClient httpClient;
    private readonly TimeProvider clock;
    private readonly JetBrainsIdeDetector detector;
    private readonly Func<string, CancellationToken, Task> launchProcess;
    private IOAuthService? oauth;

    public JetBrainsConnector()
        : this(null)
    {
    }

    public JetBrainsConnector(
        IOAuthService? oauth,
        JetBrainsSettings? settings = null,
        HttpClient? httpClient = null,
        TimeProvider? clock = null,
        JetBrainsIdeDetector? detector = null,
        Func<string, CancellationToken, Task>? launchProcess = null)
    {
        this.oauth = oauth;
        this.settings = settings ?? JetBrainsSettings.FromEnvironment();
        this.httpClient = httpClient ?? new HttpClient();
        this.clock = clock ?? TimeProvider.System;
        this.detector = detector ?? new JetBrainsIdeDetector();
        this.launchProcess = launchProcess ?? LaunchWithShellAsync;
    }

    public bool JsonOutput { get; set; }

    void IOAuthClient.InitializeOAuth(IOAuthService service)
    {
        oauth = service ?? throw new ArgumentNullException(nameof(service));
    }

    async Task<AccountConnectionResult> IAccountConnector.ConnectAsync(CancellationToken cancellationToken)
    {
        if (oauth is null)
        {
            return new AccountConnectionResult { Success = false, Error = "OAuth service is unavailable." };
        }

        try
        {
            await oauth.LoginAsync(settings.CreateOAuthProvider(), JetBrainsSettings.DefaultAccountKey,
                cancellationToken);
            return new AccountConnectionResult { Success = true, AccountId = JetBrainsSettings.DefaultAccountKey };
        }
        catch (OAuthException exception)
        {
            return new AccountConnectionResult
            {
                Success = false,
                Error = $"JetBrains sign-in failed ({exception.Failure})."
            };
        }
    }

    async Task IOwnershipProvider.SyncAsync(CancellationToken cancellationToken)
    {
        await SyncOwnershipAsync(JetBrainsSettings.DefaultAccountKey, null, cancellationToken);
    }

    IEnumerable<SkyAction> IActionProvider.GetActions(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var executable = GetProperty(item, "jetbrains.executable", "executable");
        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            yield return CreateLaunchAction(JetBrainsProducts.ResolveName(GetProperty(item, "jetbrains.product",
                "productCode")), executable);
            yield break;
        }

        var productCode = GetProperty(item, "jetbrains.product", "productCode", "product");
        if (string.IsNullOrWhiteSpace(productCode))
        {
            yield break;
        }

        var install = detector.Detect().FirstOrDefault(candidate =>
            JetBrainsProducts.MatchesInstalledIde(candidate.ProductCode, productCode));
        if (install is null)
        {
            yield break;
        }

        yield return CreateLaunchAction(install.ProductName, install.ExecutablePath);
    }

    [Command("connect")]
    public async Task<int> ConnectCommand(
        [Option("account")] string account = JetBrainsSettings.DefaultAccountKey,
        [Option("store")] string? store = null)
    {
        if (oauth is null)
        {
            await Console.Error.WriteLineAsync("JetBrains OAuth is unavailable.");
            return 3;
        }

        try
        {
            await oauth.LoginAsync(settings.CreateOAuthProvider(), account, CancellationToken.None);
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"JetBrains sign-in failed ({exception.Failure}).");
            return 2;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"JetBrains sign-in failed: {exception.Message}");
            return 3;
        }

        try
        {
            var snapshot = await SyncOwnershipAsync(account, store, CancellationToken.None);
            await WriteSnapshotAsync(Console.Out, snapshot, clock.GetUtcNow(), JsonOutput);
            return 0;
        }
        catch (Exception exception) when (exception is OAuthException or JetBrainsApiException
                                              or JetBrainsApiResponseException or IOException or JsonException)
        {
            await Console.Error.WriteLineAsync(
                "Signed in, but JetBrains licenses could not be synchronized: " + exception.Message);
            return 3;
        }
    }

    [Command("signout")]
    public async Task<int> SignOutCommand(
        [Option("account")] string account = JetBrainsSettings.DefaultAccountKey)
    {
        if (oauth is null)
        {
            await Console.Error.WriteLineAsync("JetBrains OAuth is unavailable.");
            return 3;
        }

        try
        {
            await oauth.SignOutAsync(settings.CreateOAuthProvider(), account, CancellationToken.None);
            await Console.Out.WriteLineAsync($"Signed out JetBrains account '{account}'.");
            return 0;
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"JetBrains sign-out failed ({exception.Failure}).");
            return 2;
        }
    }

    [Command("sync")]
    public async Task<int> SyncCommand(
        [Option("account")] string account = JetBrainsSettings.DefaultAccountKey,
        [Option("store")] string? store = null)
    {
        try
        {
            var snapshot = await SyncOwnershipAsync(account, store, CancellationToken.None);
            await WriteSnapshotAsync(Console.Out, snapshot, clock.GetUtcNow(), JsonOutput);
            return 0;
        }
        catch (OAuthException exception) when (exception.Failure == OAuthFailure.LoginRequired)
        {
            await Console.Error.WriteLineAsync(
                $"JetBrains account '{account}' is not connected. Run 'skycd jetbrains connect'.");
            return 2;
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"JetBrains synchronization failed ({exception.Failure}).");
            return 2;
        }
        catch (Exception exception) when (exception is JetBrainsApiException or JetBrainsApiResponseException
                                              or IOException or JsonException)
        {
            await Console.Error.WriteLineAsync($"JetBrains synchronization failed: {exception.Message}");
            return 3;
        }
    }

    [Command("status")]
    public async Task<int> StatusCommand(
        [Option("account")] string account = JetBrainsSettings.DefaultAccountKey,
        [Option("store")] string? store = null)
    {
        try
        {
            var snapshot = await GetStatusAsync(account, store, CancellationToken.None);
            await WriteSnapshotAsync(Console.Out, snapshot, clock.GetUtcNow(), JsonOutput);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            await Console.Error.WriteLineAsync($"Cannot read JetBrains ownership state: {exception.Message}");
            return 3;
        }
    }

    [Command("launch")]
    public async Task<int> LaunchCommand(
        string product,
        [Option("store")] string? store = null)
    {
        var installs = detector.Detect();
        var install = installs.FirstOrDefault(candidate =>
            JetBrainsProducts.MatchesInstalledIde(candidate.ProductCode, product));
        if (install is null)
        {
            await Console.Error.WriteLineAsync(
                $"JetBrains IDE '{product}' is not installed or was not detected.");
            return 2;
        }

        try
        {
            await launchProcess(install.ExecutablePath, CancellationToken.None);
            await Console.Out.WriteLineAsync($"Launched {install.ProductName} ({install.ExecutablePath}).");
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Cannot launch '{install.ProductName}': {exception.Message}");
            return 3;
        }
    }

    private async Task<JetBrainsOwnershipSnapshot> SyncOwnershipAsync(
        string accountKey,
        string? storePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        if (oauth is null)
        {
            throw new OAuthException(OAuthFailure.LoginRequired);
        }

        var provider = settings.CreateOAuthProvider();
        var accounts = await oauth.GetAccountsAsync(provider, cancellationToken);
        if (!accounts.Any(id => string.Equals(id, accountKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OAuthException(OAuthFailure.LoginRequired);
        }

        var token = await oauth.GetAccessTokenAsync(provider, accountKey, cancellationToken);
        var licenses = await new JetBrainsLicenseClient(httpClient)
            .GetLicensesAsync(settings, token, cancellationToken);

        var snapshot = new JetBrainsOwnershipSnapshot
        {
            AccountKey = accountKey,
            AccountEmail = licenses
                .Select(license => license.AssigneeEmail)
                .FirstOrDefault(email => !string.IsNullOrWhiteSpace(email)),
            Connected = true,
            LastSyncedUtc = clock.GetUtcNow(),
            Licenses = licenses.ToList(),
            InstalledIdes = detector.Detect().ToList()
        };

        new JetBrainsOwnershipStore(storePath).Save(snapshot);
        return snapshot;
    }

    private async Task<JetBrainsOwnershipSnapshot> GetStatusAsync(
        string accountKey,
        string? storePath,
        CancellationToken cancellationToken)
    {
        var snapshot = new JetBrainsOwnershipStore(storePath).Load();
        snapshot.InstalledIdes = detector.Detect().ToList();
        snapshot.Connected = false;

        if (oauth is not null)
        {
            try
            {
                var accounts = await oauth.GetAccountsAsync(settings.CreateOAuthProvider(), cancellationToken);
                snapshot.Connected = accounts.Any(id =>
                    string.Equals(id, accountKey, StringComparison.OrdinalIgnoreCase));
            }
            catch (OAuthException)
            {
                snapshot.Connected = false;
            }
        }

        snapshot.AccountKey = string.IsNullOrWhiteSpace(snapshot.AccountKey) ? accountKey : snapshot.AccountKey;
        return snapshot;
    }

    private static IReadOnlyList<JetBrainsProductOwnership> BuildProducts(
        JetBrainsOwnershipSnapshot snapshot,
        DateTimeOffset asOf)
    {
        return snapshot.Licenses
            .GroupBy(license => license.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var license = group
                    .OrderBy(candidate => candidate.Suspended)
                    .ThenByDescending(candidate => candidate.ExpiresUtc)
                    .First();
                var install = snapshot.InstalledIdes.FirstOrDefault(candidate =>
                    JetBrainsProducts.MatchesInstalledIde(candidate.ProductCode, license.ProductCode));
                return new JetBrainsProductOwnership
                {
                    ProductCode = license.ProductCode,
                    ProductName = string.IsNullOrWhiteSpace(license.ProductName)
                        ? JetBrainsProducts.ResolveName(license.ProductCode)
                        : license.ProductName,
                    LicenseId = license.LicenseId,
                    LicenseType = license.LicenseType,
                    ExpiresUtc = license.ExpiresUtc,
                    RenewsAutomatically = license.RenewsAutomatically,
                    Status = license.EvaluateStatus(asOf),
                    Seats = license.Seats,
                    AssigneeEmail = license.AssigneeEmail,
                    Installed = install is not null,
                    ExecutablePath = install?.ExecutablePath,
                    Version = install?.Version
                };
            })
            .OrderBy(product => product.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task WriteSnapshotAsync(
        TextWriter output,
        JetBrainsOwnershipSnapshot snapshot,
        DateTimeOffset asOf,
        bool jsonOutput)
    {
        var products = BuildProducts(snapshot, asOf);
        if (jsonOutput)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                connected = snapshot.Connected,
                account = snapshot.AccountKey,
                accountEmail = snapshot.AccountEmail,
                lastSyncedUtc = snapshot.LastSyncedUtc,
                products,
                installedIdes = snapshot.InstalledIdes
                    .OrderBy(install => install.ProductName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            }, JsonOptions));
            return;
        }

        await output.WriteLineAsync(snapshot.Connected
            ? $"JetBrains account '{snapshot.AccountKey}' is connected" +
              (string.IsNullOrWhiteSpace(snapshot.AccountEmail) ? "." : $" ({snapshot.AccountEmail}).")
            : $"JetBrains account '{snapshot.AccountKey}' is not connected. Run 'skycd jetbrains connect'.");

        if (snapshot.LastSyncedUtc is { } lastSynced)
        {
            await output.WriteLineAsync($"Last synced: {lastSynced:u}");
        }

        if (products.Count == 0)
        {
            await output.WriteLineAsync("No owned JetBrains products recorded.");
            if (snapshot.InstalledIdes.Count > 0)
            {
                await output.WriteLineAsync(
                    $"Installed JetBrains IDEs: {string.Join(", ", snapshot.InstalledIdes
                        .OrderBy(install => install.ProductName, StringComparer.OrdinalIgnoreCase)
                        .Select(install => install.ProductName))}");
            }

            return;
        }

        await output.WriteLineAsync("PRODUCT | TYPE | EXPIRES | STATUS | INSTALLED");
        foreach (var product in products)
        {
            var installed = product.Installed
                ? $"yes ({product.ExecutablePath})"
                : "no";
            await output.WriteLineAsync(
                $"{product.ProductName} | {product.LicenseType} | {FormatDate(product.ExpiresUtc)} | " +
                $"{product.Status} | {installed}");
        }

        var unowned = snapshot.InstalledIdes
            .Where(install => !products.Any(product =>
                JetBrainsProducts.MatchesInstalledIde(install.ProductCode, product.ProductCode)))
            .Select(install => install.ProductName)
            .ToArray();
        if (unowned.Length > 0)
        {
            await output.WriteLineAsync($"Installed but not owned: {string.Join(", ", unowned)}");
        }
    }

    private SkyAction CreateLaunchAction(string productName, string executablePath)
    {
        return new SkyAction("launch", $"Launch {productName}",
            cancellationToken => launchProcess(executablePath, cancellationToken));
    }

    private static string? GetProperty(Item item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (item.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd") ?? "-";

    private static Task LaunchWithShellAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
        });
        return Task.CompletedTask;
    }
}
