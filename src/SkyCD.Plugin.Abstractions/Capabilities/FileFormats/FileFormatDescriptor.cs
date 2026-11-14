using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Describes a file format supported by a plugin capability.
/// </summary>
public sealed record FileFormatDescriptor(
    string FormatId,
    string DisplayName,
    IReadOnlyCollection<string> Extensions,
    IReadOnlyCollection<string> MimeTypes,
    bool CanRead,
    bool CanWrite,
    bool SupportsFolder = false)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileFormatDescriptor"/> record without folder support.
    /// </summary>
    public FileFormatDescriptor(
        string formatId,
        string displayName,
        IReadOnlyCollection<string> extensions,
        IReadOnlyCollection<string> mimeTypes,
        bool canRead,
        bool canWrite)
        : this(formatId, displayName, extensions, mimeTypes, canRead, canWrite, false)
    {
    }
}