using System;
using System.Security.Cryptography;
using System.Text;

namespace SkyCD.Presentation.MediaCache;

/// <summary>Shared content addressing for fingerprints, record identities and artifact digests.</summary>
internal static class MediaCacheHashing
{
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256Hex(Encoding.UTF8.GetBytes(value));
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
