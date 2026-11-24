using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Actions;

/// <summary>Host-supplied item context, independent of persistence and UI models.</summary>
public sealed class Item
{
    /// <summary>Gets the stable item identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets provider-specific identifiers and metadata supplied by the host.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
}
