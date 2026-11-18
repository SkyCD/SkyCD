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

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>
/// OAuth-first Microsoft 365 connector: account sign-in, subscription sync, installed Office
/// discovery, renewal state and launch actions. Commands are exposed as <c>skycd microsoft365 ...</c>.
/// </summary>
[Command("microsoft365")]
public sealed class Microsoft365Connector : IOAuthClient, IAccountConnector, IOwnershipProvider, IActionProvider,
    ICliPluginCapability, ISupportsJsonOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Microsoft365Settings settings;
    private readonly HttpClient httpClient;
    private readonly TimeProvider clock;
    private readonly Microsoft365OfficeDetector detector;
    private readonly Func<string, CancellationToken, Task> launchProcess;
    private IOAuthService? oauth;

    public Microsoft365Connector()
        : this(null)
    {
    }

    public Microsoft365Connector(
        IOAuthService? oauth,
        Microsoft365Settings? settings = null,
        HttpClient? httpClient = null,
        TimeProvider? clock = null,
        Microsoft365OfficeDetector? detector = null,
        Func<string, CancellationToken, Task>? launchProcess = null)
    {
        this.oauth = oauth;
        this.settings = settings ?? Microsoft365Settings.FromEnvironment();
        this.httpClient = httpClient ?? new HttpClient();
        this.clock = clock ?? TimeProvider.System;
        this.detector = detector ?? new Microsoft365OfficeDetector();
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
            await oauth.LoginAsync(settings.CreateOAuthProvider(), Microsoft365Settings.DefaultAccountKey,
                cancellationToken);
            return new AccountConnectionResult
            {
                Success = true,
                AccountId = Microsoft365Settings.DefaultAccountKey
            };
        }
        catch (OAuthException exception)
        {
            return new AccountConnectionResult
            {
                Success = false,
                Error = $"Microsoft 365 sign-in failed ({exception.Failure})."
            };
        }
    }

    Task IOwnershipProvider.SyncAsync(CancellationToken cancellationToken) =>
        SyncOwnershipAsync(Microsoft365Settings.DefaultAccountKey, null, cancellationToken);

    IEnumerable<SkyAction> IActionProvider.GetActions(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var executable = GetProperty(item, "microsoft365.executable", "executable");
        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            var name = Microsoft365Products.TryResolveAppCode(executable, out var explicitCode)
                ? Microsoft365Products.ResolveAppName(explicitCode)
                : Path.GetFileNameWithoutExtension(executable);
            yield return CreateLaunchAction(name, executable);
            yield break;
        }

        var app = GetProperty(item, "microsoft365.app", "app", "productCode", "product");
        if (string.IsNullOrWhiteSpace(app) || !Microsoft365Products.TryResolveAppCode(app, out var productCode))
        {
            yield break;
        }

        var install = detector.Detect().FirstOrDefault(candidate =>
            string.Equals(candidate.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (install is null)
        {
            yield break;
        }

        yield return CreateLaunchAction(install.ProductName, install.ExecutablePath);
    }

    [Command("connect")]
    public async Task<int> ConnectCommand(
        [Option("account")] string account = Microsoft365Settings.DefaultAccountKey,
        [Option("store")] string? store = null)
    {
        if (oauth is null)
        {
            await Console.Error.WriteLineAsync("Microsoft 365 OAuth is unavailable.");
            return 3;
        }

        try
        {
            await oauth.LoginAsync(settings.CreateOAuthProvider(), account, CancellationToken.None);
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"Microsoft 365 sign-in failed ({exception.Failure}).");
            return 2;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Microsoft 365 sign-in failed: {exception.Message}");
            return 3;
        }

        try
        {
            var snapshot = await SyncOwnershipAsync(account, store, CancellationToken.None);
            await WriteSnapshotAsync(Console.Out, snapshot, clock.GetUtcNow(), JsonOutput);
            return 0;
        }
        catch (Exception exception) when (exception is OAuthException or Microsoft365ApiException
                                              or Microsoft365ApiResponseException or IOException or JsonException)
        {
            await Console.Error.WriteLineAsync(
                "Signed in, but Microsoft 365 subscriptions could not be synchronized: " + exception.Message);
            return 3;
        }
    }

    [Command("signout")]
    public async Task<int> SignOutCommand(
        [Option("account")] string account = Microsoft365Settings.DefaultAccountKey)
    {
        if (oauth is null)
        {
            await Console.Error.WriteLineAsync("Microsoft 365 OAuth is unavailable.");
            return 3;
        }

        try
        {
            await oauth.SignOutAsync(settings.CreateOAuthProvider(), account, CancellationToken.None);
            await Console.Out.WriteLineAsync($"Signed out Microsoft 365 account '{account}'.");
            return 0;
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"Microsoft 365 sign-out failed ({exception.Failure}).");
            return 2;
        }
    }

    [Command("sync")]
    public async Task<int> SyncCommand(
        [Option("account")] string account = Microsoft365Settings.DefaultAccountKey,
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
                $"Microsoft 365 account '{account}' is not connected. Run 'skycd microsoft365 connect'.");
            return 2;
        }
        catch (OAuthException exception)
        {
            await Console.Error.WriteLineAsync($"Microsoft 365 synchronization failed ({exception.Failure}).");
            return 2;
        }
        catch (Exception exception) when (exception is Microsoft365ApiException
                                              or Microsoft365ApiResponseException or IOException or JsonException)
        {
            await Console.Error.WriteLineAsync($"Microsoft 365 synchronization failed: {exception.Message}");
            return 3;
        }
    }

    [Command("status")]
    public async Task<int> StatusCommand(
        [Option("account")] string account = Microsoft365Settings.DefaultAccountKey,
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
            await Console.Error.WriteLineAsync($"Cannot read Microsoft 365 ownership state: {exception.Message}");
            return 3;
        }
    }

    [Command("launch")]
    public async Task<int> LaunchCommand(
        string app,
        [Option("store")] string? store = null)
    {
        if (!Microsoft365Products.TryResolveAppCode(app, out var productCode))
        {
            await Console.Error.WriteLineAsync($"'{app}' is not a recognized Office application.");
            return 2;
        }

        var install = detector.Detect().FirstOrDefault(candidate =>
            string.Equals(candidate.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (install is null)
        {
            await Console.Error.WriteLineAsync(
                $"Office application '{Microsoft365Products.ResolveAppName(productCode)}' is not installed or was not detected.");
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

    private async Task<Microsoft365OwnershipSnapshot> SyncOwnershipAsync(
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
        var subscriptions = await new Microsoft365GraphClient(httpClient)
            .GetSubscriptionsAsync(settings, token, cancellationToken);

        var snapshot = new Microsoft365OwnershipSnapshot
        {
            AccountKey = accountKey,
            Connected = true,
            LastSyncedUtc = clock.GetUtcNow(),
            Subscriptions = subscriptions
                .OrderBy(subscription => subscription.ProductName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(subscription => subscription.SubscriptionId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            InstalledOfficeApps = detector.Detect().ToList()
        };

        new Microsoft365OwnershipStore(storePath).Save(snapshot);
        return snapshot;
    }

    private async Task<Microsoft365OwnershipSnapshot> GetStatusAsync(
        string accountKey,
        string? storePath,
        CancellationToken cancellationToken)
    {
        var snapshot = new Microsoft365OwnershipStore(storePath).Load();
        snapshot.InstalledOfficeApps = detector.Detect().ToList();
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

    private static IReadOnlyList<Microsoft365ProductOwnership> BuildProducts(
        Microsoft365OwnershipSnapshot snapshot,
        DateTimeOffset asOf)
    {
        return snapshot.Subscriptions
            .Select(subscription =>
            {
                var linkedCodes = Microsoft365Products.GetLinkedAppCodes(subscription.SkuPartNumber);
                var linkedApps = linkedCodes.Count == 0
                    ? []
                    : snapshot.InstalledOfficeApps
                        .Where(app => linkedCodes.Contains(app.ProductCode, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(app => app.ProductName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                return new Microsoft365ProductOwnership
                {
                    SubscriptionId = subscription.SubscriptionId,
                    SkuPartNumber = subscription.SkuPartNumber,
                    ProductName = subscription.ProductName,
                    Seats = subscription.Seats,
                    RenewsUtc = subscription.RenewsUtc,
                    IsTrial = subscription.IsTrial,
                    Status = subscription.EvaluateStatus(asOf),
                    Installed = linkedApps.Count > 0,
                    InstalledApps = linkedApps
                };
            })
            .OrderBy(product => product.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(product => product.SubscriptionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task WriteSnapshotAsync(
        TextWriter output,
        Microsoft365OwnershipSnapshot snapshot,
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
                lastSyncedUtc = snapshot.LastSyncedUtc,
                subscriptions = products,
                installedOfficeApps = snapshot.InstalledOfficeApps
                    .OrderBy(app => app.ProductName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            }, JsonOptions));
            return;
        }

        await output.WriteLineAsync(snapshot.Connected
            ? $"Microsoft 365 account '{snapshot.AccountKey}' is connected."
            : $"Microsoft 365 account '{snapshot.AccountKey}' is not connected. Run 'skycd microsoft365 connect'.");

        if (snapshot.LastSyncedUtc is { } lastSynced)
        {
            await output.WriteLineAsync($"Last synced: {lastSynced:u}");
        }

        if (products.Count == 0)
        {
            await output.WriteLineAsync("No Microsoft 365 subscriptions recorded.");
            await WriteInstalledAppsAsync(output, snapshot.InstalledOfficeApps);
            return;
        }

        await output.WriteLineAsync("SUBSCRIPTION | SEATS | RENEWS | STATUS | INSTALLED APPS");
        foreach (var product in products)
        {
            var installed = product.Installed
                ? $"yes ({string.Join(", ", product.InstalledApps.Select(app => app.ProductName))})"
                : "no";
            var label = product.IsTrial ? product.ProductName + " (trial)" : product.ProductName;
            await output.WriteLineAsync(
                $"{label} | {product.Seats} | {FormatDate(product.RenewsUtc)} | " +
                $"{product.Status} | {installed}");
        }

        var linkedCodes = products
            .SelectMany(product => product.InstalledApps)
            .Select(app => app.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlinked = snapshot.InstalledOfficeApps
            .Where(app => !linkedCodes.Contains(app.ProductCode))
            .Select(app => app.ProductName)
            .ToArray();
        if (unlinked.Length > 0)
        {
            await output.WriteLineAsync($"Installed but not linked: {string.Join(", ", unlinked)}");
        }
    }

    private static async Task WriteInstalledAppsAsync(TextWriter output,
        IReadOnlyList<OfficeAppInstall> installedApps)
    {
        if (installedApps.Count > 0)
        {
            await output.WriteLineAsync(
                $"Installed Office apps: {string.Join(", ", installedApps
                    .OrderBy(app => app.ProductName, StringComparer.OrdinalIgnoreCase)
                    .Select(app => app.ProductName))}");
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
