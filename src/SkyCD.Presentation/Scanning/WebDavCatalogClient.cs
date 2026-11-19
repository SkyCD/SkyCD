using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using SkyCD.Presentation.Operations;
using WebDav;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Establishes a catalog session on top of a <see cref="WebDav.WebDavClient"/> (#625).
/// The HTTP transport, redirects and the Basic/Digest authentication handshake belong to
/// <see cref="HttpClientHandler"/>; the 207 Multi-Status parsing belongs to WebDav.Client.
/// This class only builds the request URIs, maps the responses onto the catalog session
/// contract and onto the SkyCD exception semantics. One instance serves one scheme:
/// <c>webdav://</c> (HTTP) or <c>webdavs://</c> (HTTPS).
/// </summary>
public sealed class WebDavCatalogConnector : IRemoteCatalogConnector
{
    private readonly string scheme;
    private readonly int defaultPort;
    private readonly HttpMessageHandler? handler;

    public WebDavCatalogConnector() : this("webdav", 80)
    {
    }

    /// <summary>
    /// Builds a connector for one WebDAV scheme. <paramref name="handler"/> replaces the default
    /// transport (the test seam for a fake <see cref="HttpMessageHandler"/>); when null the
    /// connector builds an <see cref="HttpClientHandler"/> with the supplied credentials,
    /// system proxy defaults and automatic redirects.
    /// </summary>
    public WebDavCatalogConnector(string scheme, int defaultPort, HttpMessageHandler? handler = null)
    {
        this.scheme = scheme;
        this.defaultPort = defaultPort;
        this.handler = handler;
    }

    public string Scheme => scheme;

    public int DefaultPort => defaultPort;

    public string NormalizePath(string? path) => FtpPath.Normalize(path);

    public bool CanConfirmCredentials(RemoteCredentials credentials) =>
        credentials.Anonymous ||
        (!string.IsNullOrWhiteSpace(credentials.Username) &&
         !string.IsNullOrWhiteSpace(credentials.Password));

    public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (credentials.Anonymous)
        {
            // Anonymous WebDAV sends no Authorization header at all.
            return (string.Empty, string.Empty);
        }

        var username = credentials.Username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(credentials.Password))
        {
            throw new RemoteAuthenticationException(scheme);
        }

        return (username, credentials.Password);
    }

    public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null)
    {
        return new RemoteConnectionException(scheme, $"{location.Host}:{location.Port}", cause,
            detail is null ? null : $" ({detail})");
    }

    /// <summary>Seconds one HTTP request may stall before it is treated as a disconnect.</summary>
    internal const int RequestTimeoutSeconds = 20;

    /// <summary>Builds the absolute request URI of a collection; the trailing slash keeps relative hrefs resolvable.</summary>
    internal static Uri RequestUri(RemoteLocation location, string path)
    {
        var normalized = FtpPath.Normalize(path);
        var builder = new UriBuilder(
            string.Equals(location.Scheme, "webdavs", StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttps
                : Uri.UriSchemeHttp,
            location.Host,
            location.Port)
        {
            Path = normalized == "/" ? "/" : normalized + "/",
        };
        return builder.Uri;
    }

    public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(credentials);

        ResolveCredentials(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        var client = new WebDavClient(CreateHttpClient(credentials));
        try
        {
            // HTTP has no login exchange, so a Depth 0 PROPFIND is the connect probe: a bad
            // host, a refused TLS session, a redirect loop or rejected credentials surface at
            // "connect" time like the other providers. A missing path is not a connect failure.
            var probe = client.Propfind(RequestUri(location, location.Path), new PropfindParameters
            {
                ApplyTo = ApplyTo.Propfind.ResourceOnly,
                CancellationToken = cancellationToken,
            }).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            if (probe.StatusCode == 401)
            {
                throw new RemoteAuthenticationException(scheme);
            }

            if (probe.StatusCode is >= 300 and < 400)
            {
                throw ConnectionFailed(location, null, DescribeStatus(probe));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is not RemoteAuthenticationException and not RemoteConnectionException)
        {
            client.Dispose();
            throw TranslateFailure(ex, location, cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new WebDavRemoteSession(client, location, cancellationToken);
    }

    private HttpClient CreateHttpClient(RemoteCredentials credentials)
    {
        var timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        if (handler is not null)
        {
            return new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
        }

        var httpHandler = new HttpClientHandler
        {
            UseDefaultCredentials = false,
        };
        if (!credentials.Anonymous)
        {
            // HttpClientHandler negotiates Basic or Digest from the server's WWW-Authenticate
            // challenge; anonymous access sends no Authorization header.
            var resolved = ResolveCredentials(credentials);
            httpHandler.Credentials = new NetworkCredential(resolved.Username, resolved.Password);
        }

        return new HttpClient(httpHandler, disposeHandler: true) { Timeout = timeout };
    }

    /// <summary>Maps a transport failure onto the SkyCD remote exception semantics.</summary>
    internal static Exception TranslateFailure(
        Exception exception,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (exception is RemoteConnectionException or RemoteAuthenticationException)
        {
            return exception;
        }

        // HttpRequestException covers unreachable hosts, TLS failures and exceeded redirect
        // limits; a request timeout surfaces as TaskCanceledException without a cancelled token.
        var detail = DescribeException(exception);
        return new RemoteConnectionException(
            location.Scheme,
            $"{location.Host}:{location.Port}",
            exception,
            detail.Length > 0 ? " " + detail : null);
    }

    /// <summary>Maps a non-success response onto the SkyCD remote exception semantics.</summary>
    internal static Exception TranslateStatus(PropfindResponse response, string path, RemoteLocation location)
    {
        return response.StatusCode switch
        {
            401 => new RemoteAuthenticationException(location.Scheme),
            403 => new RemoteListingNotSupportedException(path, "list-directory", location.Scheme),
            404 => new RemoteDirectoryNotFoundException(path),
            _ => new RemoteConnectionException(
                location.Scheme,
                $"{location.Host}:{location.Port}",
                null,
                " " + DescribeStatus(response)),
        };
    }

    /// <summary>A 207 without any response element is a malformed or missing multistatus body.</summary>
    internal static Exception MalformedListing(string path, RemoteLocation location) =>
        new RemoteListingNotSupportedException(path, "list-directory", location.Scheme);

    private static string DescribeStatus(PropfindResponse response)
    {
        var detail = string.IsNullOrWhiteSpace(response.Description)
            ? $"HTTP {response.StatusCode}"
            : $"HTTP {response.StatusCode} {response.Description}";
        return Sanitize(detail);
    }

    private static string DescribeException(Exception exception)
    {
        var detail = exception.InnerException?.Message ?? exception.Message;
        return Sanitize(detail ?? string.Empty);
    }

    private static string Sanitize(string detail) =>
        SecretSanitizer.Sanitize(detail).Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>
/// A connected catalog session. Listings are parsed by WebDav.Client; the projection onto
/// <see cref="RemoteEntry"/> keeps the catalog names, sizes and UTC timestamps.
/// </summary>
internal sealed class WebDavRemoteSession : IRemoteCatalogSession
{
    private readonly WebDavClient client;
    private readonly RemoteLocation location;
    private readonly CancellationToken cancellationToken;

    internal WebDavRemoteSession(WebDavClient client, RemoteLocation location, CancellationToken cancellationToken)
    {
        this.client = client;
        this.location = location;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<RemoteEntry> ListDirectory(string path)
    {
        var normalized = FtpPath.Normalize(path);
        cancellationToken.ThrowIfCancellationRequested();

        var requestUri = WebDavCatalogConnector.RequestUri(location, normalized);
        PropfindResponse response;
        try
        {
            response = client.Propfind(requestUri, new PropfindParameters
            {
                ApplyTo = ApplyTo.Propfind.ResourceAndChildren,
                CancellationToken = cancellationToken,
            }).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WebDavCatalogConnector.TranslateFailure(ex, location, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessful)
        {
            throw WebDavCatalogConnector.TranslateStatus(response, normalized, location);
        }

        if (response.Resources is null || response.Resources.Count == 0)
        {
            throw WebDavCatalogConnector.MalformedListing(normalized, location);
        }

        return WebDavRemoteListingMapper.Map(response.Resources, normalized, requestUri);
    }

    public void Dispose() => client.Dispose();
}
