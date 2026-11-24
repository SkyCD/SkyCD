using System;

namespace SkyCD.Documents;

public sealed class LocationNotFoundException(string locationId)
    : InvalidOperationException($"Location '{locationId}' does not exist.")
{
    public string LocationId { get; } = locationId;
}
