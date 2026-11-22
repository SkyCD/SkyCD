using System;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Per-entry fields a format can represent. Names are always represented and have no flag.
/// </summary>
[Flags]
public enum FileFormatEntryFieldSupport
{
    /// <summary>Represented fields, if any.</summary>
    None = 0,

    /// <summary>Entry identifiers.</summary>
    Id = 1 << 0,

    /// <summary>Parent relationships.</summary>
    ParentId = 1 << 1,

    /// <summary>Entry kinds.</summary>
    Kind = 1 << 2,

    /// <summary>Entry sizes in bytes.</summary>
    Size = 1 << 3,

    /// <summary>Assigned statuses.</summary>
    Status = 1 << 4,

    /// <summary>Custom entry properties.</summary>
    Properties = 1 << 5,

    /// <summary>Every entry field.</summary>
    All = Id | ParentId | Kind | Size | Status | Properties
}
