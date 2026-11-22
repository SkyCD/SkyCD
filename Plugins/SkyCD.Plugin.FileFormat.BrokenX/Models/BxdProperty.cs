using System;

namespace SkyCD.Plugin.FileFormat.BrokenX.Models;

/// <summary>
/// A single property entry. The complete descriptor byte is the stable property key.
/// </summary>
public sealed class BxdProperty
{
    public BxdProperty(byte descriptor, uint uintValue)
    {
        Descriptor = descriptor;
        Kind = BxdPropertyKind.Uint32;
        UintValue = uintValue;
    }

    public BxdProperty(byte descriptor, string text, byte[] rawBytes)
    {
        Descriptor = descriptor;
        Kind = BxdPropertyKind.Text;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        RawBytes = rawBytes ?? throw new ArgumentNullException(nameof(rawBytes));
    }

    public BxdProperty(byte descriptor, byte[] blob)
    {
        Descriptor = descriptor;
        Kind = BxdPropertyKind.Blob;
        RawBytes = blob ?? throw new ArgumentNullException(nameof(blob));
    }

    /// <summary>
    /// Gets the complete descriptor byte (payload kind in bits 7..6, property ID in bits 5..0).
    /// </summary>
    public byte Descriptor { get; }

    public BxdPropertyKind Kind { get; }

    public uint UintValue { get; }

    public string? Text { get; }

    /// <summary>
    /// Gets the original encoded payload bytes (text payloads) or the blob payload.
    /// </summary>
    public byte[]? RawBytes { get; }
}
