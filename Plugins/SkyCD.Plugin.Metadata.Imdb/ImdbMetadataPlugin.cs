using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Imdb.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.Plugin.Metadata.Imdb;

/// <summary>
/// Looks film and series entries up on IMDb. A pasted <c>/title/tt...</c> URL is fetched directly; otherwise the
/// title search endpoint is queried. Facts come primarily from the embedded JSON-LD and Open Graph data IMDb
/// serves, with a light genre-anchor pass layered on when those are absent.
/// </summary>
public sealed class ImdbMetadataPlugin : IOnlineMetadataSourceCapability
{
    /// <summary>Stable source id shared with saved GenDesc settings and template variables.</summary>
    public const string SourceIdValue = "imdb";

    private static readonly string[] Hosts = ["imdb.com"];

    private readonly IWebFetcher fetcher;

    public ImdbMetadataPlugin() : this(new HttpWebFetcher())
    {
    }

    internal ImdbMetadataPlugin(IWebFetcher fetcher) =>
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public string SourceId => SourceIdValue;

    public string DisplayName => "IMDb";

    public IReadOnlyList<string> KnownHosts => Hosts;

    internal string? ResolveUrl(OnlineMetadataRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, Hosts)) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.imdb.com/find/?q={UrlHelpers.Encode(request.Query.Trim())}&s=tt";
        return null;
    }

    internal void Parse(string body, DescriptionFacts facts) => ImdbPageParser.Parse(body, facts);

    public async Task<OnlineMetadataResult> FetchAsync(OnlineMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var url = ResolveUrl(request);
        if (url is null)
            return OnlineMetadataResult.Failure(SourceId, null, "the source cannot serve this request");

        try
        {
            var body = await fetcher.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var facts = new DescriptionFacts();
            Parse(body, facts);
            return OnlineMetadataResult.Success(SourceId, url, facts.Snapshot());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OnlineMetadataResult.Failure(SourceId, url, $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
