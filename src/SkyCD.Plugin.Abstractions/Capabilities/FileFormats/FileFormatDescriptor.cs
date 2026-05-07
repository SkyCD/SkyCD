using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Describes a file format supported by a plugin capability.
/// </summary>
public sealed record FileFormatDescriptor(
    string FormatId,
    string DisplayName,
    IReadOnlyCollection<string> Extensions,
    bool CanRead,
    bool CanWrite,
    string? MimeType = null);
