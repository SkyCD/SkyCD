using System;
using System.Text;

namespace SkyCD.Plugin.NeoFinder.Text;

/// <summary>A strict decoding profile resolved for a NeoFinder text export.</summary>
public sealed class NeoFinderTextEncodingProfile
{
    private readonly Encoding _encoding;

    private NeoFinderTextEncodingProfile(NeoFinderTextEncodingKind kind, string displayName, Encoding encoding)
    {
        Kind = kind;
        DisplayName = displayName;
        _encoding = encoding;
    }

    public NeoFinderTextEncodingKind Kind { get; }
    public string DisplayName { get; }

    public static NeoFinderTextEncodingProfile Utf8 { get; } =
        new(NeoFinderTextEncodingKind.Utf8, "UTF-8", new UTF8Encoding(false, true));

    public static NeoFinderTextEncodingProfile Utf8WithByteOrderMark { get; } =
        new(NeoFinderTextEncodingKind.Utf8WithByteOrderMark, "UTF-8 with BOM", new UTF8Encoding(true, true));

    public static NeoFinderTextEncodingProfile Utf16LittleEndian { get; } =
        new(NeoFinderTextEncodingKind.Utf16LittleEndian, "UTF-16 LE", new UnicodeEncoding(false, true, true));

    public static NeoFinderTextEncodingProfile Utf16BigEndian { get; } =
        new(NeoFinderTextEncodingKind.Utf16BigEndian, "UTF-16 BE", new UnicodeEncoding(true, true, true));

    public static NeoFinderTextEncodingProfile Windows1252 { get; } =
        new(NeoFinderTextEncodingKind.Windows1252, "Windows-1252 (ANSI)",
            CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!);

    public static NeoFinderTextEncodingProfile Unknown { get; } =
        new(NeoFinderTextEncodingKind.Unknown, "Unknown", new UTF8Encoding(false, true));

    /// <summary>Decodes without replacement characters, stripping a BOM only when present.</summary>
    public string Decode(ReadOnlySpan<byte> data)
    {
        var preamble = _encoding.GetPreamble();
        var skip = preamble.Length > 0 && data.StartsWith(preamble) ? preamble.Length : 0;
        return _encoding.GetString(data[skip..]);
    }
}
