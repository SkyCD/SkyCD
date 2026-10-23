using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Core.OAuth;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.OAuth;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Abstractions;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class OAuthTests
{
    private static OAuthProvider Provider(string clientId = "public-client", string host = "provider.example") => new()
    {
        Id = "provider",
        ClientId = clientId,
        AuthorizationEndpoint = new Uri($"https://{host}/authorize"),
        TokenEndpoint = new Uri($"https://{host}/token"),
        Scopes = ["read", "offline_access"]
    };

    [Fact]
    public async Task Login_UsesStatePkceAndEncodedForm_ThenReusesToken()
    {
        var browser = new Browser();
        var handler = new TokenEndpoint();
        using var http = new HttpClient(handler);
        var service = new OAuthService(http, new MemoryStore(), browser, TimeProvider.System).ForPlugin("plugin");
        await service.LoginAsync(Provider(), "first");
        var authorization = Query(browser.AuthorizationUri!);
        var request = Query(new Uri("https://example.test/?" + handler.Forms.Single()));
        Assert.Equal("code", authorization["response_type"]);
        Assert.Equal("S256", authorization["code_challenge_method"]);
        Assert.Equal("read offline_access", authorization["scope"]);
        Assert.Equal("public-client", request["client_id"]);
        Assert.Equal("authorization_code", request["grant_type"]);
        Assert.Equal("code+with&symbols", request["code"]);
        Assert.Equal(authorization["redirect_uri"], request["redirect_uri"]);
        Assert.InRange(request["code_verifier"].Length, 43, 128);
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(request["code_verifier"])))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(challenge, authorization["code_challenge"]);
        Assert.Equal("access-1", await service.GetAccessTokenAsync(Provider(), "first"));
        Assert.Single(handler.Forms);
    }

    [Theory]
    [InlineData("wrong-state")]
    [InlineData("duplicate-state")]
    [InlineData("wrong-path")]
    [InlineData("fragment")]
    public async Task InvalidCallbacks_NeverExchangeCode(string mode)
    {
        var handler = new TokenEndpoint();
        using var http = new HttpClient(handler);
        var service = new OAuthService(http, new MemoryStore(), new Browser(mode), TimeProvider.System).ForPlugin("plugin");
        var error = await Assert.ThrowsAsync<OAuthException>(() => service.LoginAsync(Provider(), "account"));
        Assert.Equal(OAuthFailure.InvalidCallback, error.Failure);
        Assert.Empty(handler.Forms);
    }

    [Fact]
    public async Task DeniedAuthorization_IsSanitizedAndDoesNotSaveSession()
    {
        var handler = new TokenEndpoint();
        using var http = new HttpClient(handler);
        var service = new OAuthService(http, new MemoryStore(), new Browser("denied"), TimeProvider.System).ForPlugin("plugin");
        var error = await Assert.ThrowsAsync<OAuthException>(() => service.LoginAsync(Provider(), "account"));
        Assert.Equal(OAuthFailure.AuthorizationDenied, error.Failure);
        Assert.DoesNotContain("sensitive", error.ToString());
        Assert.Empty(handler.Forms);
        Assert.Empty(await service.GetAccountsAsync(Provider()));
    }

    [Fact]
    public async Task Refresh_IsSerialized_RotatesAndPreservesRefreshTokens()
    {
        var clock = new Clock();
        var handler = new TokenEndpoint();
        using var http = new HttpClient(handler);
        var coordinator = new OAuthService(http, new MemoryStore(), new Browser(), clock);
        var service = coordinator.ForPlugin("plugin");
        await service.LoginAsync(Provider(), "account");
        clock.Now = clock.Now.AddHours(2);
        var tokens = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => coordinator.ForPlugin("plugin").GetAccessTokenAsync(Provider(), "account")));
        Assert.All(tokens, token => Assert.Equal("access-2", token));
        Assert.Equal(2, handler.Forms.Count);
        Assert.Equal("refresh-1", Query(new Uri("https://example.test/?" + handler.Forms.Last()))["refresh_token"]);
        clock.Now = clock.Now.AddHours(2);
        handler.OmitRefreshToken = true;
        await service.GetAccessTokenAsync(Provider(), "account");
        Assert.Equal("refresh-2", Query(new Uri("https://example.test/?" + handler.Forms.Last()))["refresh_token"]);
        clock.Now = clock.Now.AddHours(2);
        await service.GetAccessTokenAsync(Provider(), "account");
        Assert.Equal("refresh-2", Query(new Uri("https://example.test/?" + handler.Forms.Last()))["refresh_token"]);
    }

    [Fact]
    public async Task InvalidGrant_RemovesSession_TransientFailurePreservesIt()
    {
        var clock = new Clock();
        var handler = new TokenEndpoint();
        using var http = new HttpClient(handler);
        var service = new OAuthService(http, new MemoryStore(), new Browser(), clock).ForPlugin("plugin");
        await service.LoginAsync(Provider(), "account");
        clock.Now = clock.Now.AddHours(2);
        handler.Error = "server_error";
        Assert.Equal(OAuthFailure.TokenRequestFailed,
            (await Assert.ThrowsAsync<OAuthException>(() => service.GetAccessTokenAsync(Provider(), "account"))).Failure);
        Assert.Single(await service.GetAccountsAsync(Provider()));
        handler.Error = "invalid_grant";
        Assert.Equal(OAuthFailure.LoginRequired,
            (await Assert.ThrowsAsync<OAuthException>(() => service.GetAccessTokenAsync(Provider(), "account"))).Failure);
        Assert.Empty(await service.GetAccountsAsync(Provider()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"access_token\":\"sensitive\",\"token_type\":\"Bearer\",\"expires_in\":\"invalid\"}")]
    [InlineData("{\"access_token\":\"sensitive\",\"token_type\":\"other\",\"expires_in\":3600}")]
    public async Task MalformedTokenResponse_IsSanitized(string body)
    {
        using var http = new HttpClient(new TokenEndpoint { Body = body });
        var service = new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System).ForPlugin("plugin");
        var error = await Assert.ThrowsAsync<OAuthException>(() => service.LoginAsync(Provider(), "account"));
        Assert.Equal(OAuthFailure.InvalidTokenResponse, error.Failure);
        Assert.DoesNotContain("sensitive", error.ToString());
    }

    [Fact]
    public async Task Accounts_PluginsAndProviderConfigurations_AreIndependent()
    {
        using var http = new HttpClient(new TokenEndpoint());
        var coordinator = new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System);
        var first = coordinator.ForPlugin("first-plugin");
        var second = coordinator.ForPlugin("second-plugin");
        await first.LoginAsync(Provider(), "personal");
        await first.LoginAsync(Provider(), "work");
        await second.LoginAsync(Provider(), "personal");
        Assert.Equal(2, (await first.GetAccountsAsync(Provider())).Count);
        Assert.Single(await second.GetAccountsAsync(Provider()));
        Assert.Empty(await first.GetAccountsAsync(Provider("changed-client")));
        Assert.Empty(await first.GetAccountsAsync(Provider(host: "changed.example")));
        await first.SignOutAsync(Provider(), "personal");
        Assert.Single(await first.GetAccountsAsync(Provider()));
        Assert.Single(await second.GetAccountsAsync(Provider()));
        Assert.Equal(OAuthFailure.LoginRequired,
            (await Assert.ThrowsAsync<OAuthException>(() => first.GetAccessTokenAsync(Provider(), "personal"))).Failure);
    }

    [Fact]
    public async Task Cancellation_DoesNotPublishSession()
    {
        using var http = new HttpClient(new TokenEndpoint());
        var service = new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System).ForPlugin("plugin");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoginAsync(Provider(), "account", cts.Token));
        Assert.Empty(await service.GetAccountsAsync(Provider()));
    }

    [Fact]
    public async Task NoRefreshToken_UsesValidToken_ThenRequiresLoginAfterExpiry()
    {
        var clock = new Clock();
        using var http = new HttpClient(new TokenEndpoint { OmitRefreshToken = true });
        var service = new OAuthService(http, new MemoryStore(), new Browser(), clock).ForPlugin("plugin");
        await service.LoginAsync(Provider(), "account");
        clock.Now = clock.Now.AddSeconds(3590);
        Assert.Equal("access-1", await service.GetAccessTokenAsync(Provider(), "account"));
        clock.Now = clock.Now.AddSeconds(11);
        Assert.Equal(OAuthFailure.LoginRequired,
            (await Assert.ThrowsAsync<OAuthException>(() => service.GetAccessTokenAsync(Provider(), "account"))).Failure);
    }

    [Fact]
    public async Task ProviderOptions_AreEncoded_AndCannotOverrideProtocolFields()
    {
        using var http = new HttpClient(new TokenEndpoint());
        var browser = new Browser();
        var service = new OAuthService(http, new MemoryStore(), browser, TimeProvider.System).ForPlugin("plugin");
        var provider = new OAuthProvider
        {
            Id = "options",
            ClientId = "client",
            AuthorizationEndpoint = Provider().AuthorizationEndpoint,
            TokenEndpoint = Provider().TokenEndpoint,
            AuthorizationParameters = new Dictionary<string, string> { ["prompt"] = "select_account", ["login_hint"] = "user+test@example.test" }
        };
        await service.LoginAsync(provider, "account");
        Assert.Equal("user+test@example.test", Query(browser.AuthorizationUri!)["login_hint"]);
        foreach (var name in new[] { "state", "code_challenge", "client_id", "redirect_uri", "client_secret" })
        {
            provider = new OAuthProvider
            {
                Id = "options",
                ClientId = "client",
                AuthorizationEndpoint = Provider().AuthorizationEndpoint,
                TokenEndpoint = Provider().TokenEndpoint,
                AuthorizationParameters = new Dictionary<string, string> { [name] = "override" }
            };
            Assert.Equal(OAuthFailure.InvalidConfiguration,
                (await Assert.ThrowsAsync<OAuthException>(() => service.LoginAsync(provider, "account"))).Failure);
        }
    }

    [Fact]
    public void DefaultHostRegistration_ResolvesOAuthCoordinator()
    {
        using var container = new Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);
        Assert.NotNull(container.Resolve<OAuthService>().ForPlugin("plugin"));
    }

    [Fact]
    public async Task InsecureEndpoint_IsRejectedBeforeBrowserOrNetwork()
    {
        using var http = new HttpClient(new TokenEndpoint());
        var service = new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System).ForPlugin("plugin");
        var provider = new OAuthProvider
        {
            Id = "test",
            ClientId = "client",
            AuthorizationEndpoint = new Uri("http://example.test/authorize"),
            TokenEndpoint = new Uri("https://example.test/token")
        };
        Assert.Equal(OAuthFailure.InvalidConfiguration,
            (await Assert.ThrowsAsync<OAuthException>(() => service.LoginAsync(provider, "account"))).Failure);
    }

    [Fact]
    public async Task RealLoopbackBrowser_CompletesLoginAndClosesListener()
    {
        using var callbackHttp = new HttpClient();
        Task<HttpResponseMessage>? callback = null;
        Uri? redirect = null;
        var browser = new SystemOAuthBrowser(authorization =>
        {
            var query = Query(authorization);
            redirect = new Uri(query["redirect_uri"]);
            callback = callbackHttp.GetAsync(redirect + "?code=browser-code&state=" + query["state"]);
        });
        using var http = new HttpClient(new TokenEndpoint());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var service = new OAuthService(http, new MemoryStore(), browser, TimeProvider.System).ForPlugin("plugin");
        await service.LoginAsync(Provider(), "account", timeout.Token);
        using var response = await callback!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("browser-code", await response.Content.ReadAsStringAsync());
        Assert.Equal("access-1", await service.GetAccessTokenAsync(Provider(), "account"));
        await Assert.ThrowsAsync<HttpRequestException>(() => callbackHttp.GetAsync(redirect, timeout.Token));
    }

    [Fact]
    public async Task RealLoopbackBrowser_CancellationReleasesPort()
    {
        Uri? redirect = null;
        using var cts = new CancellationTokenSource();
        var browser = new SystemOAuthBrowser(uri => { redirect = new Uri(Query(uri)["redirect_uri"]); cts.Cancel(); });
        using var http = new HttpClient(new TokenEndpoint());
        var service = new OAuthService(http, new MemoryStore(), browser, TimeProvider.System).ForPlugin("plugin");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoginAsync(Provider(), "account", cts.Token));
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, redirect!.Port);
        listener.Start();
    }

    [Fact]
    public async Task DpapiPersistence_SurvivesRestart_EncryptsAndDetectsTampering()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<OAuthException>(() => new WindowsOAuthDataProtector().Protect([1], [2]));
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "skycd-oauth-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new TokenEndpoint();
            var clock = new Clock();
            using var http = new HttpClient(handler);
            var first = new OAuthService(http, new ProtectedFileOAuthTokenStore(folder, new WindowsOAuthDataProtector()),
                new Browser(), clock).ForPlugin("plugin");
            await first.LoginAsync(Provider(), "account");
            var files = Directory.GetFiles(folder, "*.token", SearchOption.AllDirectories);
            Assert.Single(files);
            Assert.DoesNotContain("access-1", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(files[0])));
            Assert.DoesNotContain("refresh-1", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(files[0])));
            var restarted = new OAuthService(http, new ProtectedFileOAuthTokenStore(folder, new WindowsOAuthDataProtector()),
                new Browser(), clock).ForPlugin("plugin");
            Assert.Equal("access-1", await restarted.GetAccessTokenAsync(Provider(), "account"));
            Assert.Equal(["account"], await restarted.GetAccountsAsync(Provider()));
            clock.Now = clock.Now.AddHours(2);
            Assert.Equal("access-2", await restarted.GetAccessTokenAsync(Provider(), "account"));
            var bytes = await File.ReadAllBytesAsync(files[0]);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(files[0], bytes);
            Assert.Equal(OAuthFailure.SecureStorageUnavailable,
                (await Assert.ThrowsAsync<OAuthException>(() => restarted.GetAccessTokenAsync(Provider(), "account"))).Failure);
            await restarted.SignOutAsync(Provider(), "account");
            Assert.Empty(await restarted.GetAccountsAsync(Provider()));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task DiscoveredPlugin_ReceivesHostOAuthService()
    {
        using var http = new HttpClient(new TokenEndpoint());
        var oauth = new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System);
        using var container = new Container();
        var capability = new OAuthCapability();
        var plugin = new DiscoveredPlugin
        {
            Id = "test.oauth",
            Name = "OAuth plugin",
            FileName = "test.dll",
            Version = new Version(3, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            Capabilities = [capability]
        };
        PluginServiceRegistrator.RegisterServices(container, [plugin], oauth);
        var resolved = container.Resolve<IOAuthClient>();
        Assert.Same(capability, resolved);
        await capability.Service!.LoginAsync(Provider(), "account");
        Assert.Equal("access-1", await capability.Service.GetAccessTokenAsync(Provider(), "account"));
        Assert.Empty(await oauth.ForPlugin("other.plugin").GetAccountsAsync(Provider()));
    }

    private sealed class OAuthCapability : IOAuthClient
    {
        public IOAuthService? Service { get; private set; }
        public void InitializeOAuth(IOAuthService service) => Service = service;
    }

    [Fact]
    public void ThrowingInitialization_DoesNotCrashHostRegistration()
    {
        using var http = new HttpClient(new TokenEndpoint());
        using var container = new Container();
        PluginFailure? captured = null;
        void Capture(PluginFailure failure) => captured = failure;
        PluginFailure.Reported += Capture;
        try
        {
            var plugin = new DiscoveredPlugin
            {
                Id = "test.broken",
                Name = "Broken",
                FileName = "test.dll",
                Version = new Version(3, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                Capabilities = [new ThrowingOAuthCapability()]
            };
            PluginServiceRegistrator.RegisterServices(container, [plugin],
                new OAuthService(http, new MemoryStore(), new Browser(), TimeProvider.System));
            Assert.NotNull(captured);
            Assert.DoesNotContain("sensitive", captured.ToString());
        }
        finally { PluginFailure.Reported -= Capture; }
    }

    private sealed class ThrowingOAuthCapability : IOAuthClient
    {
        public void InitializeOAuth(IOAuthService service) => throw new OAuthException(OAuthFailure.InvalidConfiguration);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Browser(string mode = "normal") : IOAuthBrowser
    {
        public Uri? AuthorizationUri { get; private set; }
        public Task<Uri> AuthorizeAsync(Func<Uri, Uri> buildAuthorizationUri, int port, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var redirect = new Uri("http://127.0.0.1:12345/oauth/callback");
            AuthorizationUri = buildAuthorizationUri(redirect);
            var state = Query(AuthorizationUri)["state"];
            var query = mode switch
            {
                "wrong-state" => "state=wrong&code=code",
                "duplicate-state" => $"state={state}&state={state}&code=code",
                "denied" => $"state={state}&error=access_denied&error_description=sensitive",
                _ => $"state={state}&code=code%2Bwith%26symbols"
            };
            return Task.FromResult(new Uri((mode == "wrong-path" ? "http://127.0.0.1:12345/wrong" : redirect.AbsoluteUri)
                + "?" + query + (mode == "fragment" ? "#fragment" : "")));
        }
    }

    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public List<string> Forms { get; } = [];
        public string? Error { get; set; }
        public string? Body { get; init; }
        public bool OmitRefreshToken { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Forms.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(Error is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(Body ?? (Error is not null ? $"{{\"error\":\"{Error}\"}}"
                    : $"{{\"access_token\":\"access-{Forms.Count}\",\"token_type\":\"Bearer\",\"expires_in\":3600"
                    + (OmitRefreshToken ? "}" : $",\"refresh_token\":\"refresh-{Forms.Count}\"}}")))
            };
        }
    }

    private sealed class MemoryStore : IOAuthTokenStore
    {
        private readonly ConcurrentDictionary<(string Namespace, string Account), OAuthSession> _sessions = new();
        public Task<OAuthSession?> ReadAsync(string namespaceId, string accountId, CancellationToken cancellationToken)
            => Task.FromResult(_sessions.GetValueOrDefault((namespaceId, accountId)));
        public Task WriteAsync(string namespaceId, OAuthSession session, CancellationToken cancellationToken)
        { _sessions[(namespaceId, session.AccountId)] = session; return Task.CompletedTask; }
        public Task<IReadOnlyList<string>> GetAccountsAsync(string namespaceId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(_sessions.Keys.Where(key => key.Namespace == namespaceId).Select(key => key.Account).ToArray());
        public Task DeleteAsync(string namespaceId, string accountId, CancellationToken cancellationToken)
        { _sessions.TryRemove((namespaceId, accountId), out _); return Task.CompletedTask; }
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
}
