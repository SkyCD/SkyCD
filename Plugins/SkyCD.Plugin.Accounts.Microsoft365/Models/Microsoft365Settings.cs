using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Plugin.Accounts.Microsoft365.Models;

/// <summary>Microsoft 365 OAuth and Graph configuration, optionally overridden by environment variables.</summary>
public sealed class Microsoft365Settings
{
    public const string DefaultAccountKey = "default";
    public const string ProviderId = "microsoft365";

    private const string ClientIdVariable = "SKYCD_MICROSOFT365_OAUTH_CLIENT_ID";
    private const string TenantVariable = "SKYCD_MICROSOFT365_TENANT";
    private const string AuthorizationEndpointVariable = "SKYCD_MICROSOFT365_OAUTH_AUTHORIZATION_ENDPOINT";
    private const string TokenEndpointVariable = "SKYCD_MICROSOFT365_OAUTH_TOKEN_ENDPOINT";
    private const string ScopesVariable = "SKYCD_MICROSOFT365_OAUTH_SCOPES";
    private const string GraphBaseVariable = "SKYCD_MICROSOFT365_GRAPH_BASE";
    private const string CompanySubscriptionsPathVariable = "SKYCD_MICROSOFT365_SUBSCRIPTIONS_PATH";
    private const string SubscribedSkusPathVariable = "SKYCD_MICROSOFT365_SKUS_PATH";

    private static readonly IReadOnlyList<string> DefaultScopes =
    [
        "offline_access",
        "User.Read",
        "Organization.Read.All",
        "Directory.Read.All"
    ];

    public string ClientId { get; init; } = string.Empty;

    public string Tenant { get; init; } = "common";

    public Uri AuthorizationEndpoint { get; init; } = DefaultAuthorizationEndpoint("common");

    public Uri TokenEndpoint { get; init; } = DefaultTokenEndpoint("common");

    public IReadOnlyList<string> Scopes { get; init; } = DefaultScopes;

    public Uri GraphBaseUri { get; init; } = new("https://graph.microsoft.com/v1.0/");

    public string CompanySubscriptionsPath { get; init; } = "directory/subscriptions";

    public string SubscribedSkusPath { get; init; } = "subscribedSkus";

    public static Microsoft365Settings FromEnvironment()
    {
        var tenant = Read(TenantVariable) ?? "common";
        return new Microsoft365Settings
        {
            ClientId = Read(ClientIdVariable) ?? string.Empty,
            Tenant = tenant,
            AuthorizationEndpoint = ReadUri(AuthorizationEndpointVariable, DefaultAuthorizationEndpoint(tenant)),
            TokenEndpoint = ReadUri(TokenEndpointVariable, DefaultTokenEndpoint(tenant)),
            Scopes = ReadScopes(),
            GraphBaseUri = ReadUri(GraphBaseVariable, "https://graph.microsoft.com/v1.0/"),
            CompanySubscriptionsPath = Read(CompanySubscriptionsPathVariable) ?? "directory/subscriptions",
            SubscribedSkusPath = Read(SubscribedSkusPathVariable) ?? "subscribedSkus"
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

    public Uri GetCompanySubscriptionsUri() => Combine(GraphBaseUri, CompanySubscriptionsPath);

    public Uri GetSubscribedSkusUri() => Combine(GraphBaseUri, SubscribedSkusPath);

    private static Uri DefaultAuthorizationEndpoint(string tenant) =>
        new($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize");

    private static Uri DefaultTokenEndpoint(string tenant) =>
        new($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");

    private static Uri Combine(Uri baseUri, string path)
    {
        var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/");
        return new Uri(normalized, path.TrimStart('/'));
    }

    private static string? Read(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value.Trim() : null;

    private static Uri ReadUri(string variable, string fallback) =>
        Uri.TryCreate(Read(variable) ?? fallback, UriKind.Absolute, out var uri)
            ? uri
            : new Uri(fallback);

    private static Uri ReadUri(string variable, Uri fallback) =>
        Uri.TryCreate(Read(variable) ?? fallback.AbsoluteUri, UriKind.Absolute, out var uri) ? uri : fallback;

    private static IReadOnlyList<string> ReadScopes()
    {
        var configured = Read(ScopesVariable);
        if (configured is null)
        {
            return DefaultScopes;
        }

        var scopes = configured
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        return scopes.Length == 0 ? DefaultScopes : scopes;
    }
}
