using System;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Opaque binary payload attached to a catalog object: stored file contents,
/// thumbnails, generated descriptions, or any kind SkyCD does not classify.
/// Unknown kinds are preserved byte-for-byte.
/// </summary>
public sealed class DedPayload
{
    public DedPayload(uint rawKind, byte[] data)
    {
        RawKind = rawKind;
        Data = data ?? [];
    }

    public DedPayload(DedPayloadKind kind, byte[] data)
        : this((uint)kind, data)
    {
    }

    /// <summary>Gets the wire kind value.</summary>
    public uint RawKind { get; }

    /// <summary>Gets the classified kind, or <see cref="DedPayloadKind.Unknown"/>.</summary>
    public DedPayloadKind Kind => RawKind switch
    {
        (uint)DedPayloadKind.StoredContent => DedPayloadKind.StoredContent,
        (uint)DedPayloadKind.Thumbnail => DedPayloadKind.Thumbnail,
        (uint)DedPayloadKind.GeneratedDescription => DedPayloadKind.GeneratedDescription,
        _ => DedPayloadKind.Unknown
    };

    /// <summary>Gets a copy of the payload bytes.</summary>
    public byte[] Data { get; }
}
