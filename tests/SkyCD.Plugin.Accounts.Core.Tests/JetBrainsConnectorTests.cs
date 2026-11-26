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
using SkyCD.SDK.Capabilities.Actions;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Ownership;
using SkyCD.SDK.OAuth;
using SkyCD.Plugin.Accounts.JetBrains;
using SkyCD.TestSupport;
using Xunit;
using SkyCD.Plugin.Accounts.JetBrains.Models;
using SkyCD.Plugin.Accounts.JetBrains.Parsing;

namespace SkyCD.Plugin.Accounts.Core.Tests;

[Collection("Console capture")]
public sealed class JetBrainsConnectorTests
{
    private const string LicensesJson = """
        [
          {
            "licenseId": "L1",
            "product": { "code": "II", "name": "IntelliJ IDEA Ultimate" },
            "subscription": { "validUntilDate": "2027-01-01T00:00:00Z", "isAutomaticallyRenewed": true },
            "assignee": { "type": "USER", "email": "dev@example.com" }
          },
          {
            "licenseId": "L2",
            "product": { "code": "PS", "name": "PhpStorm" },
            "isTrial": true
          },
          {
            "licenseId": "L3",
            "product": { "code": "WS", "name": "WebStorm" },
            "perpetual": { "upgradeDueDate": "2020-01-01T00:00:00Z" },
            "isSuspended": true
          }
        ]
        """;

    [Fact]
    public async Task AccountConnector_LogsInThroughOAuthAndReturnsAccountKey()
    {
        var oauth = new FakeOAuth();
        var connector = new JetBrainsConnector(oauth, TestSettings());

        var result = await ((IAccountConnector)connector).ConnectAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(JetBrainsSettings.DefaultAccountKey, result.AccountId);
        Assert.Equal("test-client", oauth.LastProvider!.ClientId);
        Assert.Equal(JetBrainsSettings.DefaultAccountKey, oauth.LastAccount);
    }

    [Fact]
    public async Task AccountConnector_ReportsSanitizedOAuthFailure()
    {
        var oauth = new FakeOAuth { LoginFailure = OAuthFailure.AuthorizationDenied };
        var connector = new JetBrainsConnector(oauth, TestSettings());

        var result = await ((IAccountConnector)connector).ConnectAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(nameof(OAuthFailure.AuthorizationDenied), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectCommand_ImportsLicensesAndPersistsOwnershipSnapshot()
    {
        using var fixture = new ConnectorFixture();
        var ideaPath = fixture.Install("IntelliJ IDEA Ultimate 2026.1", "idea64.exe");

        var (exitCode, output, error) =
            await CaptureAsync(() => fixture.Connector.ConnectCommand(store: fixture.StorePath));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.Contains("connected", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IntelliJ IDEA Ultimate", output, StringComparison.Ordinal);
        Assert.Equal("Bearer access-token", fixture.Handler.Authorization);
        Assert.EndsWith("/customer/licenses", fixture.Handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var snapshot = new JetBrainsOwnershipStore(fixture.StorePath).Load();
        Assert.True(snapshot.Connected);
        Assert.Equal("dev@example.com", snapshot.AccountEmail);
        Assert.Equal(3, snapshot.Licenses.Count);
        Assert.Equal(ideaPath, Assert.Single(snapshot.InstalledIdes).ExecutablePath);
    }

    [Fact]
    public async Task AccountSession_ReadsTokenFreeSnapshotWithoutNetwork()
    {
        using var fixture = new ConnectorFixture();
        var ideaPath = fixture.Install("IntelliJ IDEA Ultimate 2026.1", "idea64.exe");
        await fixture.Connector.ConnectCommand(store: fixture.StorePath);
        var session = fixture.CreateConnector((_, _) => Task.CompletedTask, fixture.StorePath);

        var status = await ((IAccountSession)session).GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsConnected);
        Assert.Equal(JetBrainsSettings.ProviderId, status.ProviderId);
        Assert.Equal("dev@example.com", status.AccountEmail);
        Assert.Equal(3, status.Products.Count);
        Assert.Equal(
            ["IntelliJ IDEA Ultimate", "PhpStorm", "WebStorm"],
            status.Products.Select(product => product.Product));
        var idea = status.Products.Single(product => product.Product == "IntelliJ IDEA Ultimate");
        Assert.Equal("active", idea.Status);
        Assert.Equal(ideaPath, idea.ExecutablePath);
        Assert.Equal("L1", idea.Id);
    }

    [Fact]
    public async Task AccountSession_SignOut_LeavesTheAccountDisconnected()
    {
        using var fixture = new ConnectorFixture();
        await fixture.Connector.ConnectCommand(store: fixture.StorePath);
        var session = (IAccountSession)fixture.CreateConnector((_, _) => Task.CompletedTask, fixture.StorePath);

        await session.SignOutAsync(CancellationToken.None);

        var status = await session.GetStatusAsync(CancellationToken.None);
        Assert.False(status.IsConnected);
    }

    [Fact]
    public async Task StatusCommand_Json_LinksInstalledIdesAndShowsSubscriptionState()
    {
        using var fixture = new ConnectorFixture();
        var ideaPath = fixture.Install("IntelliJ IDEA Ultimate 2026.1", "idea64.exe");
        await fixture.Connector.ConnectCommand(store: fixture.StorePath);
        fixture.Connector.JsonOutput = true;

        var (exitCode, output, _) =
            await CaptureAsync(() => fixture.Connector.StatusCommand(store: fixture.StorePath));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("connected").GetBoolean());
        Assert.Equal("default", root.GetProperty("account").GetString());

        var products = root.GetProperty("products").EnumerateArray().ToArray();
        var idea = products.Single(product => product.GetProperty("productCode").GetString() == "II");
        Assert.True(idea.GetProperty("installed").GetBoolean());
        Assert.Equal(ideaPath, idea.GetProperty("executablePath").GetString());
        Assert.Equal("subscription", idea.GetProperty("licenseType").GetString());
        Assert.Equal("active", idea.GetProperty("status").GetString());
        Assert.True(idea.GetProperty("renewsAutomatically").GetBoolean());

        Assert.Equal("trial", products.Single(product =>
            product.GetProperty("productCode").GetString() == "PS").GetProperty("licenseType").GetString());
        Assert.Equal("suspended", products.Single(product =>
            product.GetProperty("productCode").GetString() == "WS").GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("installedIdes").GetArrayLength());
    }

    [Fact]
    public async Task StatusCommand_WithoutSession_ReportsNotConnectedAndInstalledIdes()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);
        fixture.Install("WebStorm 2026.1", "webstorm64.exe");
        fixture.Connector.JsonOutput = true;

        var (exitCode, output, _) = await CaptureAsync(() => fixture.Connector.StatusCommand());

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("connected").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("products").EnumerateArray());
        Assert.Equal("WebStorm", document.RootElement.GetProperty("installedIdes")[0]
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
    public async Task ActionProvider_LaunchesInstalledIdeAndSkipsMissingProducts()
    {
        using var fixture = new ConnectorFixture();
        var webStormPath = fixture.Install("WebStorm 2026.1", "webstorm64.exe");
        var launched = new List<string>();
        var connector = fixture.CreateConnector((path, _) =>
        {
            launched.Add(path);
            return Task.CompletedTask;
        });

        var actions = ((IActionProvider)connector).GetActions(new Item
        {
            Id = "webstorm",
            Properties = new Dictionary<string, string> { ["productCode"] = "WS" }
        }).ToArray();

        var action = Assert.Single(actions);
        Assert.Equal("launch", action.Id);
        Assert.Contains("WebStorm", action.Title, StringComparison.Ordinal);
        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(webStormPath, Assert.Single(launched));

        Assert.Empty(((IActionProvider)connector).GetActions(new Item
        {
            Id = "rider",
            Properties = new Dictionary<string, string> { ["productCode"] = "RD" }
        }));
    }

    [Fact]
    public async Task ActionProvider_UsesExplicitExecutableProperty()
    {
        using var fixture = new ConnectorFixture();
        var executable = Path.Combine(fixture.Root, "custom-ide.exe");
        await File.WriteAllTextAsync(executable, string.Empty);
        var connector = fixture.CreateConnector((_, _) => Task.CompletedTask);

        var action = Assert.Single(((IActionProvider)connector).GetActions(new Item
        {
            Id = "custom",
            Properties = new Dictionary<string, string> { ["jetbrains.executable"] = executable }
        }));

        Assert.Equal("launch", action.Id);
    }

    [Fact]
    public async Task CliRouting_ResolvesDeclaredStatusCommand()
    {
        using var fixture = new ConnectorFixture(connectOnLogin: false);
        fixture.Install("GoLand 2026.1", "goland64.exe");
        var runner = new AppRunner<JetBrainsConnector>()
            .UseDependencyResolver(new SingleInstanceResolver(fixture.Connector));

        var (exitCode, output, _) = await CaptureAsync(
            () => runner.RunAsync(["status", "--store", fixture.StorePath]));

        Assert.Equal(0, exitCode);
        Assert.Contains("GoLand", output, StringComparison.Ordinal);
    }

    private static JetBrainsSettings TestSettings() => new()
    {
        ClientId = "test-client",
        AuthorizationEndpoint = new Uri("https://example.test/authorize"),
        TokenEndpoint = new Uri("https://example.test/token"),
        ApiBaseUri = new Uri("https://api.example.test/api/v1/")
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
        private readonly TestWorkspace workspace = new();

        public ConnectorFixture(bool connectOnLogin = true)
        {
            StorePath = Path.Combine(Root, "jetbrains.json");
            OAuth = new FakeOAuth { ConnectOnLogin = connectOnLogin };
            Handler = new LicenseHandler();
            Connector = CreateConnector((_, _) => Task.CompletedTask);
        }

        public string Root => workspace.Root;
        public string StorePath { get; }
        public FakeOAuth OAuth { get; }
        public LicenseHandler Handler { get; }
        public JetBrainsConnector Connector { get; }

        public JetBrainsConnector CreateConnector(
            Func<string, CancellationToken, Task> launcher,
            string? storePath = null) => new(
            OAuth,
            TestSettings(),
            new HttpClient(Handler),
            new FixedClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero)),
            new JetBrainsIdeDetector([Path.Combine(Root, "installs")], isWindows: true, isMacOS: false),
            launcher,
            storePath);

        public string Install(string folder, string executable)
        {
            var directory = Path.Combine(Root, "installs", folder, "bin");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, executable);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose()
        {
            Handler.Dispose();
            workspace.Dispose();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class LicenseHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LicensesJson)
            });
        }
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

    private sealed class SingleInstanceResolver(JetBrainsConnector connector) : IDependencyResolver
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

public sealed class JetBrainsIdeDetectorTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose()
    {
        workspace.Dispose();
    }

    [Fact]
    public void Detect_FindsToolboxAndStandaloneInstallsAndKeepsNewest()
    {
        var root = workspace.Root;
        CreateFile(root, Path.Combine("IntelliJ IDEA Ultimate 2024.1", "bin"), "idea64.exe");
        CreateFile(root, Path.Combine("JetBrains", "Toolbox", "apps", "IntelliJ IDEA Ultimate", "ch-0",
            "241.14494.240", "bin"), "idea64.exe");
        CreateFile(root, Path.Combine("Programs", "WebStorm 2024.2", "bin"), "webstorm64.exe");

        var installs = new JetBrainsIdeDetector([root], isWindows: true, isMacOS: false).Detect();

        Assert.Equal(2, installs.Count);
        var idea = installs.Single(install => install.ProductCode == "II");
        Assert.Equal("241.14494.240", idea.Version);
        Assert.Equal("2024.2", installs.Single(install => install.ProductCode == "WS").Version);
    }

    [Fact]
    public void Detect_MissingRoots_ReturnsEmpty()
    {
        var installs = new JetBrainsIdeDetector(
            [workspace.GetPath("missing-root")],
            isWindows: true,
            isMacOS: false).Detect();

        Assert.Empty(installs);
    }

    private static void CreateFile(string root, string relativeDirectory, string fileName)
    {
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), string.Empty);
    }
}
