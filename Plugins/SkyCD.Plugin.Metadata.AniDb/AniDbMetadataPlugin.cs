using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.AniDb.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.Plugin.Metadata.AniDb;

/// <summary>
/// Looks anime entries up on AniDB. A pasted <c>/anime/...</c> URL is fetched directly; otherwise the site
/// search is queried. It combines the shared JSON-LD / Open Graph extraction with AniDB's infobox definition
/// list, which carries type, episodes, runtime, studio and staff that the structured data often omits.
/// </summary>
public sealed class AniDbMetadataPlugin : IOnlineMetadataSourceCapability
{
    /// <summary>Stable source id shared with saved GenDesc settings and template variables.</summary>
    public const string SourceIdValue = "anidb";

    private static readonly string[] Hosts = ["anidb.net", "anidb.info"];

    private readonly IWebFetcher fetcher;

    public AniDbMetadataPlugin() : this(new HttpWebFetcher())
    {
    }

    internal AniDbMetadataPlugin(IWebFetcher fetcher) =>
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public string SourceId => SourceIdValue;

    public string DisplayName => "AniDB";

    public IReadOnlyList<string> KnownHosts => Hosts;

    internal string? ResolveUrl(OnlineMetadataRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, Hosts)) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://anidb.net/search/?q={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    internal void Parse(string body, DescriptionFacts facts) => AniDbPageParser.Parse(body, facts);

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
