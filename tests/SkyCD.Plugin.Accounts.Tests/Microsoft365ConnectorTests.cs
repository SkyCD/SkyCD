using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using CommandDotNet.Builders;
using SkyCD.Plugin.Abstractions.Capabilities.Actions;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Abstractions.Capabilities.Ownership;
using SkyCD.Plugin.Abstractions.OAuth;
using SkyCD.Plugin.Accounts.Microsoft365;
using Xunit;

namespace SkyCD.Plugin.Accounts.Tests;

[Collection("Console capture")]
public sealed class Microsoft365ConnectorTests
{
    private const string SubscriptionsJson = """
        {
          "value": [
            {
              "id": "sub-e5",
              "skuId": "sku-e5",
              "skuPartNumber": "SPE_E5",
              "totalLicenses": 25,
              "nextLifecycleDateTime": "2027-06-01T00:00:00Z",
              "status": "Enabled",
              "isTrial": false
            },
            {
              "id": "sub-business",
              "skuId": "sku-business",
              "skuPartNumber": "O365_BUSINESS_PREMIUM",
              "totalLicenses": 10,
              "nextLifecycleDateTime": "2026-12-01T00:00:00Z",
              "status": "Enabled",
              "isTrial": true
            },
            {
              "id": "sub-exchange",
              "skuId": "sku-exchange",
              "skuPartNumber": "EXCHANGESTANDARD",
              "totalLicenses": 5,
              "nextLifecycleDateTime": "2025-01-01T00:00:00Z",
              "status": "Suspended",
              "isTrial": false
            }
          ]
        }
        """;

    private const string SubscribedSkusJson = """
        {
          "value": [
            {
              "id": "tenant_sku-e5",
              "skuId": "sku-e5",
              "skuPartNumber": "SPE_E5",
              "capabilityStatus": "Enabled",
              "consumedUnits": 10,
              "prepaidUnits": { "enabled": 25, "suspended": 0, "warning": 0 }
            }
          ]
        }
        """;

    [Fact]
    public async Task AccountConnector_LogsInThroughOAuthAndReturnsAccountKey()
    {
        var oauth = new FakeOAuth();
        var connector = new Microsoft365Connector(oauth, TestSettings());

        var result = await ((IAccountConnector)connector).ConnectAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Microsoft365Settings.DefaultAccountKey, result.AccountId);
        Assert.Equal("microsoft365", oauth.LastProvider!.Id);
        Assert.Equal("test-client", oauth.LastProvider.ClientId);
        Assert.Equal(Microsoft365Settings.DefaultAccountKey, oauth.LastAccount);
    }

    [Fact]
    public async Task AccountConnector_ReportsSanitizedOAuthFailure()
    {
        var oauth = new FakeOAuth { LoginFailure = OAuthFailure.AuthorizationDenied };
        var connector = new Microsoft365Connector(oauth, TestSettings());

        var result = await ((IAccountConnector)connector).ConnectAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(nameof(OAuthFailure.AuthorizationDenied), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectCommand_ImportsSubscriptionsAndPersistsOwnershipSnapshot()
    {
        using var fixture = new ConnectorFixture();
        var wordPath = fixture.Install("WINWORD.EXE");
        var excelPath = fixture.Install("EXCEL.EXE");

        var (exitCode, output, error) =
            await CaptureAsync(() => fixture.Connector.ConnectCommand(store: fixture.StorePath));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.Contains("connected", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft 365 E5", output, StringComparison.Ordinal);
        Assert.Equal("Bearer access-token", fixture.Handler.Authorization);
        Assert.EndsWith("/directory/subscriptions", fixture.Handler.Requests.Last().AbsolutePath,
            StringComparison.Ordinal);

        var snapshot = new Microsoft365OwnershipStore(fixture.StorePath).Load();
        Assert.True(snapshot.Connected);
        Assert.Equal(3, snapshot.Subscriptions.Count);
        Assert.Equal(2, snapshot.InstalledOfficeApps.Count);
        Assert.Contains(snapshot.InstalledOfficeApps, app =>
            app.ProductCode == "WINWORD" && app.ExecutablePath == wordPath);
        Assert.Contains(snapshot.InstalledOfficeApps, app =>
            app.ProductCode == "EXCEL" && app.ExecutablePath == excelPath);
    }

    [Fact]
    public async Task StatusCommand_Json_LinksInstalledOfficeAndShowsRenewalState()
    {
        using var fixture = new ConnectorFixture();
        var wordPath = fixture.Install("WINWORD.EXE");
        await fixture.Connector.ConnectCommand(store: fixture.StorePath);
        fixture.Connector.JsonOutput = true;

        var (exitCode, output, _) =
            await CaptureAsync(() => fixture.Connector.StatusCommand(store: fixture.StorePath));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("connected").GetBoolean());
        Assert.Equal("default", root.GetProperty("account").GetString());

        var subscriptions = root.GetProperty("subscriptions").EnumerateArray().ToArray();
        Assert.Equal(3, subscriptions.Length);

        var e5 = subscriptions.Single(item => item.GetProperty("skuPartNumber").GetString() == "SPE_E5");
        Assert.Equal("active", e5.GetProperty("status").GetString());
        Assert.Equal(25, e5.GetProperty("seats").GetInt32());
        Assert.True(e5.GetProperty("installed").GetBoolean());
        var installed = e5.GetProperty("installedApps").EnumerateArray().ToArray();
        Assert.Contains(installed, app => app.GetProperty("executablePath").GetString() == wordPath);

        var business = subscriptions.Single(item =>
            item.GetProperty("skuPartNumber").GetString() == "O365_BUSINESS_PREMIUM");
        Assert.Equal("expiring", business.GetProperty("status").GetString());
        Assert.True(business.GetProperty("isTrial").GetBoolean());

        var exchange = subscriptions.Single(item =>
            item.GetProperty("skuPartNumber").GetString() == "EXCHANGESTANDARD");
        Assert.Equal("suspended", exchange.GetProperty("status").GetString());
        Assert.False(exchange.GetProperty("installed").GetBoolean());
        Assert.Equal(0, exchange.GetProperty("installedApps").GetArrayLength());

        Assert.Equal(1, root.GetProperty("installedOfficeApps").GetArrayLength());
    }

    [Fact]
    public async Task StatusCommand_WithoutSession_ReportsNotConnectedAndInstalledApps()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);
        fixture.Install("EXCEL.EXE");
        fixture.Connector.JsonOutput = true;

        var (exitCode, output, _) = await CaptureAsync(() => fixture.Connector.StatusCommand());

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("connected").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("subscriptions").EnumerateArray());
        Assert.Equal("Excel", document.RootElement.GetProperty("installedOfficeApps")[0]
            .GetProperty("productName").GetString());
    }

    [Fact]
    public async Task SyncCommand_WithoutSession_ReturnsLoginRequiredAndLeavesStoreUntouched()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);

        var (exitCode, _, error) = await CaptureAsync(() => fixture.Connector.SyncCommand(store: fixture.StorePath));

        Assert.Equal(2, exitCode);
        Assert.Contains("not connected", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.StorePath));
    }

    [Fact]
    public async Task OwnershipProvider_SyncWithoutSession_ThrowsLoginRequired()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            ((IOwnershipProvider)fixture.Connector).SyncAsync(CancellationToken.None));

        Assert.Equal(OAuthFailure.LoginRequired, exception.Failure);
    }

    [Fact]
    public async Task GraphClient_FallsBackToSubscribedSkusWithoutRenewalDate()
    {
        using var fixture = new ConnectorFixture();
        fixture.Handler.DirectoryFails = true;

        var (exitCode, _, _) = await CaptureAsync(() => fixture.Connector.ConnectCommand(store: fixture.StorePath));

        Assert.Equal(0, exitCode);
        Assert.Contains(fixture.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/subscribedSkus",
            StringComparison.Ordinal));
        var subscription = Assert.Single(new Microsoft365OwnershipStore(fixture.StorePath).Load().Subscriptions);
        Assert.Equal("SPE_E5", subscription.SkuPartNumber);
        Assert.Equal(25, subscription.Seats);
        Assert.Null(subscription.RenewsUtc);
        Assert.Equal("no-renewal", subscription.EvaluateStatus(FixedClock.Now));
    }

    [Fact]
    public async Task ConnectCommand_GraphFailure_ReturnsSanitizedError()
    {
        using var fixture = new ConnectorFixture();
        fixture.Handler.DirectoryFails = true;
        fixture.Handler.SkusFail = true;

        var (exitCode, _, error) =
            await CaptureAsync(() => fixture.Connector.ConnectCommand(store: fixture.StorePath));

        Assert.Equal(3, exitCode);
        Assert.Contains("status 403", error, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.StorePath));
    }

    [Fact]
    public async Task ActionProvider_LaunchesInstalledOfficeAppAndSkipsMissingProducts()
    {
        using var fixture = new ConnectorFixture();
        var wordPath = fixture.Install("WINWORD.EXE");
        var launched = new List<string>();
        var connector = fixture.CreateConnector((path, _) =>
        {
            launched.Add(path);
            return Task.CompletedTask;
        });

        var actions = ((IActionProvider)connector).GetActions(new Item
        {
            Id = "word",
            Properties = new Dictionary<string, string> { ["microsoft365.app"] = "WINWORD" }
        }).ToArray();

        var action = Assert.Single(actions);
        Assert.Equal("launch", action.Id);
        Assert.Contains("Word", action.Title, StringComparison.Ordinal);
        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(wordPath, Assert.Single(launched));

        Assert.Empty(((IActionProvider)connector).GetActions(new Item
        {
            Id = "excel",
            Properties = new Dictionary<string, string> { ["app"] = "EXCEL" }
        }));
    }

    [Fact]
    public async Task ActionProvider_UsesExplicitExecutableProperty()
    {
        using var fixture = new ConnectorFixture();
        var executable = Path.Combine(fixture.Root, "custom-office.exe");
        await File.WriteAllTextAsync(executable, string.Empty);
        var connector = fixture.CreateConnector((_, _) => Task.CompletedTask);

        var action = Assert.Single(((IActionProvider)connector).GetActions(new Item
        {
            Id = "custom",
            Properties = new Dictionary<string, string> { ["microsoft365.executable"] = executable }
        }));

        Assert.Equal("launch", action.Id);
    }

    [Fact]
    public async Task CliRouting_ResolvesDeclaredStatusCommand()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);
        fixture.Install("WINWORD.EXE");
        var runner = new AppRunner<Microsoft365Connector>()
            .UseDependencyResolver(new SingleInstanceResolver(fixture.Connector));

        var (exitCode, output, _) = await CaptureAsync(
            () => runner.RunAsync(["status", "--store", fixture.StorePath]));

        Assert.Equal(0, exitCode);
        Assert.Contains("Word", output, StringComparison.Ordinal);
    }

    private static Microsoft365Settings TestSettings() => new()
    {
        ClientId = "test-client",
        Tenant = "test",
        AuthorizationEndpoint = new Uri("https://example.test/authorize"),
        TokenEndpoint = new Uri("https://example.test/token"),
        GraphBaseUri = new Uri("https://graph.example.test/v1.0/")
    };

    private static async Task<(int ExitCode, string Output, string Error)> CaptureAsync(Func<Task<int>> action)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = await action();
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private sealed class ConnectorFixture : IDisposable
    {
        private readonly Dictionary<string, string> appPaths = new(StringComparer.OrdinalIgnoreCase);

        public ConnectorFixture(bool connectOnLogin = true)
        {
            Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            StorePath = Path.Combine(Root, "microsoft365.json");
            OAuth = new FakeOAuth { ConnectOnLogin = connectOnLogin };
            Handler = new GraphHandler();
            Connector = CreateConnector((_, _) => Task.CompletedTask);
        }

        public string Root { get; }
        public string StorePath { get; }
        public FakeOAuth OAuth { get; }
        public GraphHandler Handler { get; }
        public Microsoft365Connector Connector { get; }

        public Microsoft365Connector CreateConnector(Func<string, CancellationToken, Task> launcher) => new(
            OAuth,
            TestSettings(),
            new HttpClient(Handler),
            new FixedClock(),
            new Microsoft365OfficeDetector(
                () => appPaths,
                () => new Microsoft365OfficePackage
                {
                    ReleaseId = "O365ProPlusRetail",
                    ProductName = Microsoft365Products.ResolvePackageName("O365ProPlusRetail"),
                    Version = "16.0.17928.20156"
                },
                isWindows: true),
            launcher);

        public string Install(string executable)
        {
            var directory = Path.Combine(Root, "installs", "Office16");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, executable);
            File.WriteAllText(path, string.Empty);
            appPaths[executable] = path;
            return path;
        }

        public void Dispose()
        {
            Handler.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public static readonly DateTimeOffset Now =
            new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class GraphHandler : HttpMessageHandler
    {
        public bool DirectoryFails { get; set; }
        public bool SkusFail { get; set; }
        public string? Authorization { get; private set; }
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Requests.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/directory/subscriptions", StringComparison.Ordinal))
            {
                return Task.FromResult(DirectoryFails
                    ? Error(HttpStatusCode.Forbidden)
                    : Json(SubscriptionsJson));
            }

            if (path.EndsWith("/subscribedSkus", StringComparison.Ordinal))
            {
                return Task.FromResult(SkusFail
                    ? Error(HttpStatusCode.Forbidden)
                    : Json(SubscribedSkusJson));
            }

            return Task.FromResult(Error(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private static HttpResponseMessage Error(HttpStatusCode statusCode) =>
            new(statusCode) { Content = new StringContent("sensitive provider response") };
    }

    private sealed class FakeOAuth : IOAuthService
    {
        private readonly HashSet<string> accounts = new(StringComparer.OrdinalIgnoreCase);

        public bool ConnectOnLogin { get; init; } = true;
        public OAuthFailure? LoginFailure { get; init; }
        public OAuthProvider? LastProvider { get; private set; }
        public string? LastAccount { get; private set; }

        public Task LoginAsync(OAuthProvider provider, string accountId,
            CancellationToken cancellationToken = default)
        {
            if (LoginFailure is { } failure)
            {
                throw new OAuthException(failure);
            }

            LastProvider = provider;
            LastAccount = accountId;
            if (ConnectOnLogin)
            {
                accounts.Add(accountId);
            }

            return Task.CompletedTask;
        }

        public Task<string> GetAccessTokenAsync(OAuthProvider provider, string accountId,
            CancellationToken cancellationToken = default)
        {
            LastProvider = provider;
            LastAccount = accountId;
            return Task.FromResult("access-token");
        }

        public Task<IReadOnlyList<string>> GetAccountsAsync(OAuthProvider provider,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(accounts.ToArray());

        public Task SignOutAsync(OAuthProvider provider, string accountId,
            CancellationToken cancellationToken = default)
        {
            accounts.Remove(accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class SingleInstanceResolver(Microsoft365Connector connector) : IDependencyResolver
    {
        public object Resolve(Type type) =>
            type.IsInstanceOfType(connector) ? connector : Activator.CreateInstance(type)!;

        public bool TryResolve(Type type, out object instance)
        {
            if (type.IsInstanceOfType(connector))
            {
                instance = connector;
                return true;
            }

            instance = null!;
            return false;
        }
    }
}

public sealed class Microsoft365OfficeDetectorTests
{
    [Fact]
    public void Detect_FindsRegisteredAppsAndClickToRunPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wordPath = CreateFile(root, "WINWORD.EXE");
            var excelPath = CreateFile(root, "EXCEL.EXE");
            var unknownPath = CreateFile(root, "UNKNOWN.EXE");
            var missingPath = Path.Combine(root, "OUTLOOK.EXE");
            var appPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WINWORD.EXE"] = wordPath,
                ["EXCEL.EXE"] = excelPath,
                ["UNKNOWN.EXE"] = unknownPath,
                ["OUTLOOK.EXE"] = missingPath
            };
            var detector = new Microsoft365OfficeDetector(
                () => appPaths,
                () => new Microsoft365OfficePackage
                {
                    ReleaseId = "O365ProPlusRetail",
                    ProductName = Microsoft365Products.ResolvePackageName("O365ProPlusRetail"),
                    Version = "16.0.17928.20156"
                },
                isWindows: true);

            var installs = detector.Detect();

            Assert.Equal(2, installs.Count);
            var excel = installs.Single(install => install.ProductCode == "EXCEL");
            Assert.Equal("Excel", excel.ProductName);
            Assert.Equal(excelPath, excel.ExecutablePath);
            Assert.Equal("16.0.17928.20156", excel.Version);
            Assert.Equal("Microsoft 365 Apps for enterprise", excel.Suite);
            Assert.Contains(installs, install => install.ProductCode == "WINWORD");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_NonWindows_ReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wordPath = CreateFile(root, "WINWORD.EXE");
            var detector = new Microsoft365OfficeDetector(
                () => new Dictionary<string, string> { ["WINWORD.EXE"] = wordPath },
                () => null,
                isWindows: false);

            Assert.Empty(detector.Detect());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_MissingAppPaths_ReturnsEmpty()
    {
        var detector = new Microsoft365OfficeDetector(
            () => new Dictionary<string, string>(),
            () => null,
            isWindows: true);

        Assert.Empty(detector.Detect());
    }

    private static string CreateFile(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}

public sealed class Microsoft365ProductsTests
{
    [Theory]
    [InlineData("SPE_E5", "Microsoft 365 E5")]
    [InlineData("custom_sku", "custom_sku")]
    [InlineData(null, "Unknown plan")]
    public void ResolveName_MapsKnownSkuPartNumbers(string? sku, string expected) =>
        Assert.Equal(expected, Microsoft365Products.ResolveName(sku));

    [Fact]
    public void GetLinkedAppCodes_LinksSuitesAndAddOnsOnly()
    {
        Assert.Contains("WINWORD", Microsoft365Products.GetLinkedAppCodes("SPE_E5"));
        Assert.Contains("WINWORD", Microsoft365Products.GetLinkedAppCodes("O365_BUSINESS_PREMIUM"));
        Assert.Equal(new[] { "VISIO" }, Microsoft365Products.GetLinkedAppCodes("VISIOCLIENT"));
        Assert.Equal(new[] { "WINPROJ" }, Microsoft365Products.GetLinkedAppCodes("PROJECTPROFESSIONAL"));
        Assert.Empty(Microsoft365Products.GetLinkedAppCodes("EXCHANGESTANDARD"));
        Assert.Empty(Microsoft365Products.GetLinkedAppCodes(null));
    }

    [Theory]
    [InlineData("WINWORD.EXE", "WINWORD")]
    [InlineData("winword", "WINWORD")]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", "EXCEL")]
    [InlineData("\"C:\\Program Files\\Microsoft Office\\WINWORD.EXE\"", "WINWORD")]
    [InlineData(@"\\server\share\Office\POWERPNT.EXE", "POWERPNT")]
    [InlineData("/opt/office/OUTLOOK.EXE", "OUTLOOK")]
    [InlineData(@"C:\Office/root\Office16/EXCEL.EXE", "EXCEL")]
    public void TryResolveAppCode_AcceptsCodesAndExecutables(string value, string expected)
    {
        Assert.True(Microsoft365Products.TryResolveAppCode(value, out var code));
        Assert.Equal(expected, code);
    }

    [Fact]
    public void TryResolveAppCode_RejectsUnknownValues() =>
        Assert.False(Microsoft365Products.TryResolveAppCode("notepad.exe", out _));
}
