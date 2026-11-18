using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Plugin.Accounts.JetBrains;

/// <summary>JetBrains OAuth and Account API configuration, optionally overridden by environment variables.</summary>
public sealed class JetBrainsSettings
{
    public const string DefaultAccountKey = "default";
    public const string ProviderId = "jetbrains";

    private const string ClientIdVariable = "SKYCD_JETBRAINS_OAUTH_CLIENT_ID";
    private const string AuthorizationEndpointVariable = "SKYCD_JETBRAINS_OAUTH_AUTHORIZATION_ENDPOINT";
    private const string TokenEndpointVariable = "SKYCD_JETBRAINS_OAUTH_TOKEN_ENDPOINT";
    private const string ScopesVariable = "SKYCD_JETBRAINS_OAUTH_SCOPES";
    private const string ApiBaseVariable = "SKYCD_JETBRAINS_API_BASE";
    private const string LicensesPathVariable = "SKYCD_JETBRAINS_LICENSES_PATH";

    public string ClientId { get; init; } = "ide";

    public Uri AuthorizationEndpoint { get; init; } = new("https://account.jetbrains.com/oauth/login");

    public Uri TokenEndpoint { get; init; } =
        new("https://oauth.account.jetbrains.com/api/rest/oauth2/token");

    public IReadOnlyList<string> Scopes { get; init; } = ["profile", "email"];

    public Uri ApiBaseUri { get; init; } = new("https://account.jetbrains.com/api/v1/");

    public string LicensesPath { get; init; } = "customer/licenses";

    public static JetBrainsSettings FromEnvironment()
    {
        return new JetBrainsSettings
        {
            ClientId = Read(ClientIdVariable) ?? "ide",
            AuthorizationEndpoint = ReadUri(AuthorizationEndpointVariable,
                "https://account.jetbrains.com/oauth/login"),
            TokenEndpoint = ReadUri(TokenEndpointVariable,
                "https://oauth.account.jetbrains.com/api/rest/oauth2/token"),
            Scopes = ReadScopes(),
            ApiBaseUri = ReadUri(ApiBaseVariable, "https://account.jetbrains.com/api/v1/"),
            LicensesPath = Read(LicensesPathVariable) ?? "customer/licenses"
        };
    }

    public OAuthProvider CreateOAuthProvider()
    {
        return new OAuthProvider
        {
            Id = ProviderId,
            ClientId = ClientId,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            Scopes = Scopes
        };
    }

    public Uri GetLicensesUri()
    {
        var baseUri = ApiBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? ApiBaseUri
            : new Uri(ApiBaseUri.AbsoluteUri + "/");
        return new Uri(baseUri, LicensesPath.TrimStart('/'));
    }

    private static string? Read(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value.Trim() : null;

    private static Uri ReadUri(string variable, string fallback) =>
        Uri.TryCreate(Read(variable) ?? fallback, UriKind.Absolute, out var uri)
            ? uri
            : new Uri(fallback);

    private static IReadOnlyList<string> ReadScopes()
    {
        var configured = Read(ScopesVariable);
        if (configured is null)
        {
            return ["profile", "email"];
        }

        var scopes = configured
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        return scopes.Length == 0 ? ["profile", "email"] : scopes;
    }
}
