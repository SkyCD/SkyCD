using System;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Import;

/// <summary>
/// One web source the plugin can import facts from. The contract separates a pure, offline-testable
/// <see cref="Parse"/> from URL selection, so every importer's HTML or JSON handling is verifiable against a
/// fixture without any network. Adding a source means adding an implementation, not touching the orchestrator.
/// </summary>
public interface IWebSourceImporter
{
    /// <summary>Stable identifier matching a value in <see cref="GenDescSettings.KnownSources"/>.</summary>
    string SourceId { get; }

    /// <summary>
    /// Resolves the document URL to fetch for a request, or null when this source cannot serve it (for example,
    /// an explicit URL belonging to another site, or no query at all). Returning null skips the source.
    /// </summary>
    string? ResolveUrl(ImportRequest request);

    /// <summary>
    /// Extracts facts from a fetched body into <paramref name="facts"/>. Implementations must be pure and
    /// tolerant: malformed or unrelated content simply sets nothing rather than throwing, and never overwrites
    /// a fact already present.
    /// </summary>
    void Parse(string body, ImportRequest request, DescriptionFacts facts);
}
