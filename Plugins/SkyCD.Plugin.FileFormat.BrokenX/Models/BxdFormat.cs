namespace SkyCD.Plugin.FileFormat.BrokenX.Models;

/// <summary>
/// Archive constants for Broken X Database files, archive version <c>0x40</c>.
/// </summary>
public static class BxdFormat
{
    /// <summary>
    /// Exact 21-byte file signature: <c>Broken X Database\r\n\x1A</c>.
    /// </summary>
    public static readonly byte[] Signature =
    [
        0x42, 0x72, 0x6F, 0x6B, 0x65, 0x6E, 0x20, 0x58, 0x20, 0x44, 0x61, 0x74, 0x61, 0x62, 0x61, 0x73,
        0x65, 0x0D, 0x0A, 0x1A, 0x00
    ];

    public const int SignatureLength = 21;

    /// <summary>
    /// Archive version covered by this implementation.
    /// </summary>
    public const byte SupportedVersion = 0x40;

    /// <summary>
    /// Structural flag bits. They describe the preorder position only and are recomputed on write.
    /// </summary>
    public const ushort LastSiblingFlag = 0x2000;
    public const ushort NoChildrenFlag = 0x4000;
    public const ushort StructuralMask = 0x6000;

    /// <summary>
    /// Reserved base flag bit marking a password-protected (encrypted) record.
    /// </summary>
    /// <remarks>
    /// The marker bit is a codec convention: genuine-file verification is still open (see
    /// <c>BINARY-LAYOUT.md</c>). Detection fails loudly rather than silently importing protected data.
    /// </remarks>
    public const ushort ProtectedRecordFlag = 0x0001;

    /// <summary>
    /// Maximum tree depth accepted while reading (the original format uses at most 64 levels).
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// Maximum item records accepted in a single database.
    /// </summary>
    public const int MaxItemCount = 1_000_000;

    /// <summary>
    /// Maximum bytes accepted for a single string payload.
    /// </summary>
    public const int MaxStringBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Maximum bytes accepted for a single raw (blob) payload.
    /// </summary>
    public const int MaxBlobBytes = 64 * 1024 * 1024;

    /// <summary>
    /// String length encoding thresholds.
    /// </summary>
    public const int ExtendedLengthMarker = 255;
    public const int LongLengthMarker = 65535;
}
