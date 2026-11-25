using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.OnlineMetadata.Sources;

/// <summary>
/// The production <see cref="IWebFetcher"/>. A single <see cref="HttpClient"/> is shared across calls for
/// connection reuse; each request carries the caller's cancellation, which also enforces the per-source
/// timeout, and a non-success status surfaces as an exception so the source records a failure.
/// </summary>
internal sealed class HttpWebFetcher : IWebFetcher
{
    private const string UserAgent = "SkyCD/3.0 OnlineMetadata (+https://github.com/SkyCD/SkyCD)";

    private readonly HttpClient http;

    public HttpWebFetcher() : this(null)
    {
    }

    /// <summary>Accepts a caller-owned client (tests, or a host that wants to govern egress) or creates one.</summary>
    public HttpWebFetcher(HttpClient? httpClient) =>
        http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
