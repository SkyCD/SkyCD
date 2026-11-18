using System;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// One tagged extension record inside an object. Unknown tags are retained and
/// re-emitted in their original order so read-modify-write round trips stay
/// lossless.
/// </summary>
public sealed class DedExtensionField
{
    public DedExtensionField(uint tag, byte[] data)
    {
        Tag = tag;
        Data = data ?? [];
    }

    /// <summary>Gets the extension tag; zero is reserved for opaque tails.</summary>
    public uint Tag { get; }

    /// <summary>Gets a copy of the tag payload.</summary>
    public byte[] Data { get; }
}
