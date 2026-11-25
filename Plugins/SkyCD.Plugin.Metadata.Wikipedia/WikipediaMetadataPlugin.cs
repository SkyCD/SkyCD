using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Wikipedia.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.Plugin.Metadata.Wikipedia;

/// <summary>
/// Looks media entries up on Wikipedia through its public JSON APIs, which avoid HTML scraping entirely. A
/// direct <c>/wiki/Title</c> URL is resolved to the REST summary; otherwise the action API looks the title up
/// (following redirects) and returns a plain-text lead section. Only JSON is consumed, so parsing is stable.
/// </summary>
public sealed class WikipediaMetadataPlugin : IOnlineMetadataSourceCapability
{
    /// <summary>Stable source id shared with saved GenDesc settings and template variables.</summary>
    public const string SourceIdValue = "wikipedia";

    private static readonly string[] Hosts = ["wikipedia.org"];

    private readonly IWebFetcher fetcher;

    public WikipediaMetadataPlugin() : this(new HttpWebFetcher())
    {
    }

    internal WikipediaMetadataPlugin(IWebFetcher fetcher) =>
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));

    public string SourceId => SourceIdValue;

    public string DisplayName => "Wikipedia";

    public IReadOnlyList<string> KnownHosts => Hosts;

    internal string? ResolveUrl(OnlineMetadataRequest request)
    {
        var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim().ToLowerInvariant();
        if (UrlHelpers.MatchesHost(request.Url, Hosts))
        {
            var title = WikipediaTitleFromUrl(request.Url!);
            if (!string.IsNullOrWhiteSpace(title))
                return $"https://{language}.wikipedia.org/api/rest_v1/page/summary/{UrlHelpers.Encode(title)}";
        }
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&redirects=1" +
                   $"&prop=extracts|pageimages&exintro&explaintext&piprop=thumbnail&pithumbsize=640&titles={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    internal void Parse(string body, DescriptionFacts facts) => WikipediaPageParser.Parse(body, facts);

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

    private static string? WikipediaTitleFromUrl(string url)
    {
        var path = new Uri(url, UriKind.Absolute).AbsolutePath;
        var marker = "/wiki/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;
        var tail = path[(index + marker.Length)..];
        var hash = tail.IndexOf('#');
        if (hash >= 0) tail = tail[..hash];
        return Uri.UnescapeDataString(tail.Replace('_', ' ')).Trim();
    }
}
