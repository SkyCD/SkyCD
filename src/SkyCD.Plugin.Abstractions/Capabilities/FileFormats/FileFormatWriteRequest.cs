namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
///     Input payload for file format write operations.
/// </summary>
public sealed class FileFormatWriteRequest
{
    /// <summary>
    ///     Gets the target stream where plugin writes serialized content.
    /// </summary>
    public required Stream Target { get; init; }

    /// <summary>
    ///     Gets the format identifier chosen by host routing.
    /// </summary>
    public required string FormatId { get; init; }

    /// <summary>
    ///     Gets optional file name metadata.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    ///     Gets the domain payload to serialize.
    /// </summary>
    public required object Payload { get; init; }

    /// <summary>
    ///     Gets optional progress reporter (0-100).
    /// </summary>
    public IProgress<int>? Progress { get; init; }
}