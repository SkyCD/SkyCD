using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.AnimeNewsNetwork.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.Plugin.Metadata.AnimeNewsNetwork;

/// <summary>
/// Looks anime entries up on the Anime News Network encyclopedia. A pasted <c>anime.php?id=</c> URL is fetched
/// directly; otherwise the encyclopedia search is queried. It uses the shared meta/JSON-LD extraction plus
/// ANN's two-column profile table, whose label rows give type, episodes, runtime, studio and staff.
/// </summary>
public sealed class AnimeNewsNetworkMetadataPlugin : IOnlineMetadataSourceCapability
{
    /// <summary>Stable source id shared with saved GenDesc settings and template variables.</summary>
    public const string SourceIdValue = "ann";

    private static readonly string[] Hosts = ["animenewsnetwork.com"];

    private readonly IWebFetcher fetcher;

    public AnimeNewsNetworkMetadataPlugin() : this(new HttpWebFetcher())
    {
    }

    internal AnimeNewsNetworkMetadataPlugin(IWebFetcher fetcher) =>
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public string SourceId => SourceIdValue;

    public string DisplayName => "Anime News Network";

    public IReadOnlyList<string> KnownHosts => Hosts;

    internal string? ResolveUrl(OnlineMetadataRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, Hosts)) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.animenewsnetwork.com/encyclopedia/search.php?query={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    internal void Parse(string body, DescriptionFacts facts) => AnimeNewsNetworkPageParser.Parse(body, facts);

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
