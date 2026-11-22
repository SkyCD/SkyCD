using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Cab.Exceptions;

/// <summary>
/// Raised when an entry belongs to a folder compressed with a method that has no decoder.
/// The entry is still listed, but its data is never extracted.
/// </summary>
public sealed class CabUnsupportedEntryException(string entryName, int compressionType)
    : IOException($"Cabinet entry '{entryName}' uses unsupported compression type {compressionType}.")
{
    /// <summary>Gets the entry name that could not be extracted.</summary>
    public string EntryName { get; } = entryName;

    /// <summary>Gets the cabinet compression type that has no decoder.</summary>
    public int CompressionType { get; } = compressionType;
}
