using System;
using System.IO;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Input payload for extracting an archive to a temporary directory.
/// </summary>
public sealed class ArchiveExtractionRequest
{
    /// <summary>Gets the source stream containing archive content.</summary>
    public Stream Source { get; init; } = Stream.Null;

    /// <summary>Gets optional file name metadata.</summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Gets an optional parent directory for the temporary extraction. The plugin creates a unique
    /// child directory inside it. When omitted, a unique child directory under the system temp path is used.
    /// </summary>
    public string? TargetDirectory { get; init; }

    /// <summary>Gets optional progress reporter (0-100).</summary>
    public IProgress<int>? Progress { get; init; }
}
