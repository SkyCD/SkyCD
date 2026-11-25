using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.TvCom.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.Plugin.Metadata.TvCom;

/// <summary>
/// Looks television entries up on TV.com (and the CBS show pages it now points to). A pasted show URL is
/// fetched directly; otherwise the site search is queried. It relies on the shared Open Graph and JSON-LD
/// extraction, which those pages expose, with heading and summary fallbacks when neither is present.
/// </summary>
public sealed class TvComMetadataPlugin : IOnlineMetadataSourceCapability
{
    /// <summary>Stable source id shared with saved GenDesc settings and template variables.</summary>
    public const string SourceIdValue = "tvcom";

    private static readonly string[] Hosts = ["tv.com", "cbs.com"];

    private readonly IWebFetcher fetcher;

    public TvComMetadataPlugin() : this(new HttpWebFetcher())
    {
    }

    internal TvComMetadataPlugin(IWebFetcher fetcher) =>
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public string SourceId => SourceIdValue;

    public string DisplayName => "TV.com";

    public IReadOnlyList<string> KnownHosts => Hosts;

    internal string? ResolveUrl(OnlineMetadataRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, Hosts)) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.tv.com/search/?q={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    internal void Parse(string body, DescriptionFacts facts) => TvComPageParser.Parse(body, facts);

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
