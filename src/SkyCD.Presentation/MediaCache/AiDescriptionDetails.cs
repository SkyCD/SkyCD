using System;

namespace SkyCD.Presentation.MediaCache;

/// <summary>Offline view of one probably-AI description and its stored provenance.</summary>
public sealed record AiDescriptionDetails(
    string EntryId,
    AiDescriptionState State,
    string? Text,
    string? Agent,
    string? Model,
    string? PluginVersion,
    string? PromptVersion,
    string? Language,
    string? Origin,
    string? ProcessingDestination,
    DateTimeOffset? GeneratedAtUtc,
    string? FailureReason)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public static AiDescriptionDetails Unavailable(string entryId) =>
        new(entryId, AiDescriptionState.Unavailable, null, null, null, null, null, null, null, null, null, null);
}
