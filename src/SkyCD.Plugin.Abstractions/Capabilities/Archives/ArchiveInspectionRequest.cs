using System.IO;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Input payload for archive inspection operations.
/// </summary>
public sealed class ArchiveInspectionRequest
{
    /// <summary>Gets the source stream containing archive content.</summary>
    public Stream Source { get; init; } = Stream.Null;

    /// <summary>Gets optional file name metadata.</summary>
    public string? FileName { get; init; }
}
