namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>The referenced catalog entry does not exist.</summary>
public sealed class LocationApiEntryNotFoundException(string entryId)
    : LocationApiException($"Catalog entry '{entryId}' does not exist.")
{
    /// <summary>The requested catalog entry id.</summary>
    public string EntryId { get; } = entryId;
}
