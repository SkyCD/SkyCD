using System;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Models;

/// <summary>
/// The outcome of one importer run. A failure is captured as data rather than thrown so one broken source
/// never stops the others; the orchestrator keeps every result for diagnostics and provenance.
/// </summary>
public sealed record WebSourceResult
{
    private WebSourceResult(string sourceId, string url, bool succeeded, DescriptionFacts? facts, string? error)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Source id is required.", nameof(sourceId));
        SourceId = sourceId;
        Url = url ?? string.Empty;
        Succeeded = succeeded;
        Facts = facts ?? new DescriptionFacts();
        Error = error;
    }

    /// <summary>Stable importer identifier (for example <c>wikipedia</c>).</summary>
    public string SourceId { get; }

    /// <summary>The URL the importer resolved or fetched, empty when it never reached the network.</summary>
    public string Url { get; }

    /// <summary>True when the source returned at least one usable fact.</summary>
    public bool Succeeded { get; }

    /// <summary>Facts the source produced; empty when it failed or found nothing.</summary>
    public DescriptionFacts Facts { get; }

    /// <summary>Sanitized failure reason, or null on success.</summary>
    public string? Error { get; }

    public static WebSourceResult Success(string sourceId, string url, DescriptionFacts facts) =>
        new(sourceId, url, true, facts, null);

    public static WebSourceResult Failure(string sourceId, string? url, string reason) =>
        new(sourceId, url ?? string.Empty, false, null, Sanitize(reason));

    private static string Sanitize(string reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}
