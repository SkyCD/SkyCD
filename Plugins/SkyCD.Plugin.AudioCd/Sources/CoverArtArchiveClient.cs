using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.AudioCd.Sources;

/// <summary>
/// Cover Art Archive front image lookup. The requested size selects the largest thumbnail that still covers
/// it, and a missing image returns null so that a disc without artwork still imports.
/// </summary>
public sealed class CoverArtArchiveClient : ICoverArtLookup
{
    public const string BaseUrl = "https://coverartarchive.org/release/";

    private readonly HttpClient http;
    private readonly IRequestThrottle throttle;
    private readonly TimeSpan requestTimeout;

    public CoverArtArchiveClient(HttpClient http, IRequestThrottle throttle, TimeSpan requestTimeout)
    {
        this.http = http;
        this.throttle = throttle;
        this.requestTimeout = requestTimeout;
    }

    public async Task<CoverArtImage?> FetchAsync(string releaseId, int size, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return null;
        }

        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var timed = AudioCdHttp.BeginTimedRequest(requestTimeout, cancellationToken);
        var url = $"{BaseUrl}{Uri.EscapeDataString(releaseId)}/{SelectVariant(size)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", AudioCdHttp.UserAgent);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timed?.Token ?? cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var data = await AudioCdHttp.ReadBoundedBytesAsync(response, timed?.Token ?? cancellationToken)
            .ConfigureAwait(false);
        if (data is null || data.Length == 0)
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        return new CoverArtImage(data, string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
    }

    /// <summary>Maps a target size to the smallest documented thumbnail that is at least that large.</summary>
    internal static string SelectVariant(int size) => size switch
    {
        <= 250 => "front-250",
        <= 500 => "front-500",
        _ => "front-1200"
    };
}
