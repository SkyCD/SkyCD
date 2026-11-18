using System;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Encodings a DED text field can carry on disk. Values are part of the wire
/// contract and must not be renumbered.
/// </summary>
public enum DedStringEncoding : uint
{
    Windows1252 = 0,
    Utf8 = 1
}

/// <summary>
/// Text value plus the encoding it used on disk. Legacy databases are
/// Windows-1252; <see cref="DedStringEncoding.Utf8"/> is written only when a
/// value cannot be represented with Windows-1252.
/// </summary>
public sealed class DedText : IEquatable<DedText>
{
    public static readonly DedText Empty = new(string.Empty, DedStringEncoding.Windows1252);

    public DedText(string? value, DedStringEncoding encoding = DedStringEncoding.Windows1252)
    {
        Value = value ?? string.Empty;
        Encoding = encoding;
    }

    public string Value { get; }

    public DedStringEncoding Encoding { get; }

    public bool IsEmpty => Value.Length == 0;

    public static DedText FromAnsi(string? value) => new(value, DedStringEncoding.Windows1252);

    public static DedText FromUtf8(string? value) => new(value, DedStringEncoding.Utf8);

    public static DedText FromString(string? value) =>
        value is null ? Empty : DedTextCodec.ChooseEncoding(value);

    public static implicit operator DedText?(string? value) =>
        value is null ? null : FromString(value);

    public override string ToString() => Value;

    public bool Equals(DedText? other) =>
        other is not null && Encoding == other.Encoding && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DedText);

    public override int GetHashCode() => HashCode.Combine(Value, Encoding);
}
