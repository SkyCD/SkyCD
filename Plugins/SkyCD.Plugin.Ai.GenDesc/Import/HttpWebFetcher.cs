using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Ai.GenDesc.Import;

/// <summary>
/// The production <see cref="IWebFetcher"/>. A single <see cref="HttpClient"/> is shared across calls for
/// connection reuse; each request carries its own timeout and cancellation, and a non-success status surfaces
/// as an exception so the orchestrator isolates that source.
/// </summary>
public sealed class HttpWebFetcher : IWebFetcher
{
    private const string UserAgent = "SkyCD/3.0 GenDesc (+https://github.com/SkyCD/SkyCD)";

    private readonly HttpClient http;

    public HttpWebFetcher() : this(null)
    {
    }

    /// <summary>Accepts a caller-owned client (tests, or a host that wants to govern egress) or creates one.</summary>
    public HttpWebFetcher(HttpClient? httpClient) => http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<string> GetStringAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero) linked.CancelAfter(timeout);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
    }
}
