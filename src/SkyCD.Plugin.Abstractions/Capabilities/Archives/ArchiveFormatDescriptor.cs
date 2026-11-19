using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Describes an archive format supported by an archive inspection capability.
/// </summary>
public sealed record ArchiveFormatDescriptor(
    string FormatId,
    string DisplayName,
    IReadOnlyCollection<string> Extensions,
    IReadOnlyCollection<string> MimeTypes,
    ArchiveLimits Limits)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveFormatDescriptor"/> record with default limits.
    /// </summary>
    public ArchiveFormatDescriptor(
        string formatId,
        string displayName,
        IReadOnlyCollection<string> extensions,
        IReadOnlyCollection<string> mimeTypes)
        : this(formatId, displayName, extensions, mimeTypes, ArchiveLimits.Default)
    {
    }
}
