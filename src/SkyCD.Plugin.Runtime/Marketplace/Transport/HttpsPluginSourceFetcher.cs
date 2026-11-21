using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Transport;

/// <summary>
/// Anonymous HTTPS reader for catalog documents. Redirects are followed manually so every hop is checked
/// against the HTTPS-only rule, the body is read incrementally so the byte limit is enforced while
/// streaming, and every failure is mapped to a <c>SkyCD.Plugin.Runtime.Marketplace.Exceptions</c> type.
/// Host-side plugin downloads are handled by the installer (#803), never by this fetcher.
/// </summary>
public sealed class HttpsPluginSourceFetcher : IPluginSourceFetcher, IDisposable
{
    /// <summary>Maximum redirect hops followed for one document.</summary>
    public const int MaxRedirects = 5;

    private readonly HttpClient client;
    private readonly TimeSpan requestTimeout;

    public HttpsPluginSourceFetcher(HttpMessageHandler handler, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), requestTimeout,
                "The request timeout must be positive.");
        }

        this.requestTimeout = requestTimeout;
        client = new HttpClient(handler, disposeHandler: true)
        {
            // Each request sets its own linked timeout so the per-document limit is independent of the client.
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SkyCD-PluginCatalog/1.0");
    }

    public static HttpsPluginSourceFetcher CreateDefault(PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = options.RequestTimeout < TimeSpan.FromSeconds(10)
                ? options.RequestTimeout
                : TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpsPluginSourceFetcher(handler, options.RequestTimeout);
    }

    public async Task<string> GetStringAsync(Uri address, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        EnsureHttps(address);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);

        var current = address;
        try
        {
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects)
                    {
                        throw new PluginSourceProtocolException(current);
                    }

                    current = ResolveRedirect(current, response);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new PluginSourceUnavailableException(current,
                        new HttpRequestException(HttpRequestError.Unknown, $"Server answered 404 for '{current}'.",
                            null, response.StatusCode));
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new PluginSourceUnavailableException(current,
                        new HttpRequestException(HttpRequestError.Unknown,
                            $"Server answered {(int)response.StatusCode} for '{current}'.", null,
                            response.StatusCode));
                }

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength > maxBytes)
                {
                    throw new PluginSourceResponseTooLargeException(current, maxBytes);
                }

                return await ReadBoundedAsync(response.Content, current, maxBytes, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PluginSourceUnavailableException(address,
                new TimeoutException($"The request to '{address}' timed out after {requestTimeout}."));
        }
        catch (HttpRequestException exception)
        {
            throw new PluginSourceUnavailableException(address, exception);
        }
        catch (IOException exception)
        {
            throw new PluginSourceUnavailableException(address, exception);
        }
    }

    public void Dispose()
    {
        client.Dispose();
    }

    private static void EnsureHttps(Uri address)
    {
        if (!address.IsAbsoluteUri || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(address.UserInfo))
        {
            throw new PluginSourceProtocolException(address);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }

    private static Uri ResolveRedirect(Uri current, HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        if (location is null)
        {
            throw new PluginSourceProtocolException(current);
        }

        var target = location.IsAbsoluteUri ? location : new Uri(current, location);
        EnsureHttps(target);
        return target;
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, Uri address, long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new PluginSourceResponseTooLargeException(address, maxBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
