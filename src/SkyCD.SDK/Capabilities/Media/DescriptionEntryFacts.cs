using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// The host-resolved facts of one catalog file: identity, MIME type, local path facts and every metadata
/// value captured for the entry by metadata plugins.
/// </summary>
/// <param name="FileName">The catalog file name, including its extension.</param>
/// <param name="Extension">The lower-case extension including the dot, or an empty string.</param>
/// <param name="MimeType">The resolved MIME type, or an empty string when unknown.</param>
/// <param name="Facts">Case-insensitive metadata facts; the host additionally supplies name and size facts.</param>
public sealed record DescriptionEntryFacts(
    string FileName,
    string Extension,
    string MimeType,
    IReadOnlyDictionary<string, string> Facts);

/// <summary>
/// Host API that resolves a catalog entry into template facts. A description generator uses it instead of
/// reading the catalog or the media cache directly, so the plugin stays independent of host storage.
/// </summary>
public interface IDescriptionEntryFactsApi
{
    /// <summary>
    /// Returns the facts of the selected local file, or null when the entry no longer exists or is not a
    /// local file. Network entries, folders, links and media entries are rejected by the host.
    /// </summary>
    Task<DescriptionEntryFacts?> GetEntryFactsAsync(string entryId, CancellationToken cancellationToken = default);
}

/// <summary>Optional capability hook the host calls with its description entry-facts API.</summary>
public interface IDescriptionEntryFactsConsumer : IPluginCapability
{
    /// <summary>Receives the host entry-facts API for the host process lifetime.</summary>
    void InitializeDescriptionEntryFacts(IDescriptionEntryFactsApi entryFacts);
}
