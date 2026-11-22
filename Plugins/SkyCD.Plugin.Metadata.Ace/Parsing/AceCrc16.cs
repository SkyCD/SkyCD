using System;
using System.Text;

namespace SkyCD.Plugin.Metadata.Ace.Parsing;

/// <summary>
/// The 16-bit checksum ACE stores in every header: the lower 16 bits of the ACE CRC-32 of the header
/// bytes starting at the header type.
/// </summary>
internal static class AceCrc16
{
    /// <summary>Computes the ACE CRC-16 of <paramref name="data"/>.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data) => (ushort)AceCrc32.Compute(data);
}

/// <summary>
/// Decodes the OEM/ASCII name bytes stored in ACE headers. ACE predates Unicode names, so invalid UTF-8
/// sequences fall back to the Latin-1 code page instead of producing replacement characters.
/// </summary>
internal static class AceNameEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
}
