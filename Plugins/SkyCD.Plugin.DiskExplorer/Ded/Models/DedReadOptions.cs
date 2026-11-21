using System;

namespace SkyCD.Plugin.DiskExplorer.Ded.Models;

/// <summary>
/// Bounded-read configuration. Every allocation derived from an untrusted count
/// or length is checked against these limits before memory is reserved.
/// </summary>
public sealed class DedReadOptions
{
    /// <summary>Gets the maximum number of 32-bit offset-table slots.</summary>
    public int MaxTableEntries { get; init; } = 4_000_000;

    /// <summary>Gets the maximum number of objects in one database.</summary>
    public int MaxObjects { get; init; } = 1_000_000;

    /// <summary>Gets the maximum serialized object record size in bytes.</summary>
    public int MaxRecordBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the maximum size of a single text field in bytes.</summary>
    public int MaxStringBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Gets the maximum size of a single opaque payload in bytes.</summary>
    public int MaxPayloadBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the maximum file size buffered when the stream is not seekable.</summary>
    public long MaxBufferedFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets an optional password. The protection algorithm is not implemented;
    /// the bytes are never copied, logged, or retained by the reader.
    /// </summary>
    public ReadOnlyMemory<byte>? Password { get; init; }
}
