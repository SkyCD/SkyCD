namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
///     Input payload for file format read operations.
/// </summary>
public sealed class FileFormatReadRequest
{
    /// <summary>
    ///     Gets the source stream containing file content.
    /// </summary>
    public required Stream Source { get; init; }

    /// <summary>
    ///     Gets the format identifier chosen by host routing.
    /// </summary>
    public required string FormatId { get; init; }

    /// <summary>
    ///     Gets optional file name metadata.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    ///     Gets optional progress reporter (0-100).
    /// </summary>
    public IProgress<int>? Progress { get; init; }
}