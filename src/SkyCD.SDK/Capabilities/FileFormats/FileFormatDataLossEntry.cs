using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Domain-neutral snapshot of one catalog entry handed to a plugin for loss assessment.
/// </summary>
/// <param name="Id">Entry identifier.</param>
/// <param name="ParentId">Parent entry identifier, or null for a root entry.</param>
/// <param name="Name">Entry display name.</param>
/// <param name="Kind">Entry kind such as File, Folder or Media.</param>
/// <param name="Size">Entry size in bytes.</param>
/// <param name="Status">Assigned status name, when any.</param>
/// <param name="Properties">Custom property values keyed by property name.</param>
public sealed record FileFormatDataLossEntry(
    string Id,
    string? ParentId,
    string Name,
    string Kind,
    long Size,
    string? Status,
    IReadOnlyDictionary<string, string?> Properties);
