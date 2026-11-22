using System;

namespace SkyCD.Plugin.FileFormat.Adc.Models;

/// <summary>
/// One preserved optional property entry. The six-bit identifier is the stable key;
/// the two high bits of the on-disk descriptor select <see cref="Kind"/>.
/// </summary>
public sealed class AdcProperty
{
    public AdcProperty(byte id, uint uintValue)
    {
        Id = id;
        Kind = AdcPropertyKind.Uint32;
        UintValue = uintValue;
    }

    public AdcProperty(byte id, string text)
    {
        Id = id;
        Kind = AdcPropertyKind.String;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public AdcProperty(byte id, byte[] blob)
    {
        Id = id;
        Kind = AdcPropertyKind.Blob;
        Blob = blob ?? throw new ArgumentNullException(nameof(blob));
    }

    /// <summary>
    /// Gets the six-bit property identifier.
    /// </summary>
    public byte Id { get; }

    public AdcPropertyKind Kind { get; }

    public uint UintValue { get; }

    public string? Text { get; }

    public byte[]? Blob { get; }

    /// <summary>
    /// Gets the complete on-disk descriptor byte.
    /// </summary>
    public byte Descriptor => (byte)(((byte)Kind << 6) | Id);
}
