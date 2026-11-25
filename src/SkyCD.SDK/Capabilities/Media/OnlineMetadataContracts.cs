using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// What an online metadata source is asked to look up: a search query derived from the entry, an optional
/// explicit source URL the user attached, and the language the metadata should be produced in.
/// </summary>
public sealed record OnlineMetadataRequest(string? Query, string? Url, string Language)
{
    /// <summary>A request is usable when it has either a query or an explicit URL to resolve.</summary>
    public bool HasLookup => !string.IsNullOrWhiteSpace(Query) || IsAbsoluteHttpUrl(Url);

    /// <summary>True only for an http(s) absolute URL, so a malformed value never reaches a source.</summary>
    public static bool IsAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// The outcome of one source lookup. A failure is captured as data rather than thrown so one broken site
/// never stops the others; the host keeps every result for diagnostics and provenance.
/// </summary>
public sealed record OnlineMetadataResult
{
    private static readonly IReadOnlyDictionary<string, string> NoFacts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private OnlineMetadataResult(string sourceId, string? sourceUrl, bool succeeded,
        IReadOnlyDictionary<string, string> facts, string? error)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Source id is required.", nameof(sourceId));
        SourceId = sourceId;
        SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl;
        Succeeded = succeeded;
        Facts = facts;
        Error = error;
    }

    /// <summary>Stable source identifier (for example <c>wikipedia</c>).</summary>
    public string SourceId { get; }

    /// <summary>The document URL the source fetched, or null when it never reached the network.</summary>
    public string? SourceUrl { get; }

    /// <summary>True when the source returned at least one usable fact.</summary>
    public bool Succeeded { get; }

    /// <summary>Named facts the source produced; empty when it failed or found nothing.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; }

    /// <summary>Sanitized failure reason, or null on success.</summary>
    public string? Error { get; }

    /// <summary>Records a source that resolved a document and returned facts.</summary>
    public static OnlineMetadataResult Success(string sourceId, string? sourceUrl,
        IReadOnlyDictionary<string, string> facts) =>
        new(sourceId, sourceUrl, true,
            facts ?? throw new ArgumentNullException(nameof(facts)), null);

    /// <summary>Records a source that could not produce facts, with a sanitized reason when one is known.</summary>
    public static OnlineMetadataResult Failure(string sourceId, string? sourceUrl, string? error) =>
        new(sourceId, sourceUrl, false, NoFacts, Sanitize(error));

    private static string? Sanitize(string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}

/// <summary>
/// One site that looks a media entry up online and returns named facts. The host aggregates every enabled
/// implementation and hands the aggregate to description generators, so a generator owns templates and
/// rendering, never site parsing or fetching.
/// </summary>
public interface IOnlineMetadataSourceCapability : IPluginCapability
{
    /// <summary>Stable identifier shared with saved generator settings and template variables.</summary>
    string SourceId { get; }

    /// <summary>Human-readable source name for diagnostics.</summary>
    string DisplayName { get; }

    /// <summary>Domains this source owns, used to recognize a pasted URL that belongs to it.</summary>
    IReadOnlyList<string> KnownHosts { get; }

    /// <summary>
    /// Resolves the request to a document, fetches it, and returns the facts found or a failure result.
    /// Implementations must honor the cancellation token; ordinary failures are returned, not thrown.
    /// </summary>
    Task<OnlineMetadataResult> FetchAsync(OnlineMetadataRequest request, CancellationToken cancellationToken);
}

/// <summary>The merged facts plus the per-source outcomes of one online lookup pass.</summary>
public sealed record OnlineMetadataRun(IReadOnlyDictionary<string, string> Facts,
    IReadOnlyList<OnlineMetadataResult> Results);

/// <summary>
/// Host-provided access to the enabled online metadata sources, aggregated like the media extension
/// managers. Generators depend on this facade instead of owning the source implementations.
/// </summary>
public interface IOnlineMetadataService
{
    /// <summary>Source ids the host can serve right now, in a stable order.</summary>
    IReadOnlyList<string> SourceIds { get; }

    /// <summary>Known hosts of every available source, for recognizing a pasted source URL.</summary>
    IReadOnlyList<string> KnownHosts { get; }

    /// <summary>
    /// Runs the requested sources in order, merging facts so an earlier source wins a shared key, and isolates
    /// every source so a failure or timeout in one cannot stop the others.
    /// </summary>
    Task<OnlineMetadataRun> FetchAsync(IReadOnlyList<string> sourceIds, OnlineMetadataRequest request,
        TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Optional capability hook the host calls, mirroring <c>IPluginStorageConsumer</c>, to give a plugin the
/// online metadata aggregate before its services are published.
/// </summary>
public interface IOnlineMetadataConsumer : IPluginCapability
{
    /// <summary>Receives the host online metadata service for the host process lifetime.</summary>
    void InitializeOnlineMetadata(IOnlineMetadataService service);
}
