namespace SkyCD.Plugin.Adc;

/// <summary>
/// Constants for ElcomSoft Advanced Disk Catalog databases (<c>.cat</c>, <c>.adc</c>).
/// </summary>
/// <remarks>
/// All integers are little-endian. The complete decoder, including record framing and
/// parent-reference rules, is documented in <c>FORMAT.md</c>. The signature follows the
/// PRONOM fmt/1370 identifier for Advanced Disk Catalog.
/// </remarks>
public static class AdcFormat
{
    /// <summary>
    /// ASCII signature <c>ADC.Catalog</c> placed immediately after the 32-bit header length.
    /// </summary>
    public static readonly byte[] Signature =
    [
        0x41, 0x44, 0x43, 0x2E, 0x43, 0x61, 0x74, 0x61, 0x6C, 0x6F, 0x67
    ];

    /// <summary>
    /// Length value stored before <see cref="Signature"/>.
    /// </summary>
    public const uint SignatureLength = 11;

    /// <summary>
    /// Total fixed header size in bytes.
    /// </summary>
    public const int HeaderSize = 0x37;

    public const int SignatureOffset = 0x04;
    public const int VersionMajorOffset = 0x0F;
    public const int VersionMinorOffset = 0x11;
    public const int FlagsOffset = 0x13;
    public const int RecordCountOffset = 0x17;
    public const int BodyOffsetOffset = 0x1B;
    public const int CommentTableOffsetOffset = 0x23;
    public const int CommentCountOffset = 0x2B;
    public const int CategoryCountOffset = 0x2F;
    public const int ReservedOffset = 0x33;

    /// <summary>
    /// Supported format major version. Any minor version is accepted.
    /// </summary>
    public const ushort SupportedVersionMajor = 1;

    /// <summary>
    /// Size of one framed record header (<c>uint32 type</c>, <c>uint32 payloadLength</c>).
    /// </summary>
    public const int RecordHeaderSize = 8;

    public const uint RecordTypeVolume = 0x0001;
    public const uint RecordTypeItem = 0x0002;
    public const uint RecordTypeCategory = 0x0003;

    /// <summary>Maximum bytes accepted for one catalog file.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Maximum framed records accepted in one catalog.</summary>
    public const int MaxRecordCount = 1_000_000;

    /// <summary>Maximum UTF-16 code units accepted for one string field.</summary>
    public const int MaxStringChars = 1024 * 1024;

    /// <summary>Maximum bytes accepted for one preserved property blob.</summary>
    public const int MaxBlobBytes = 64 * 1024 * 1024;

    /// <summary>Maximum preserved properties accepted on one volume or item.</summary>
    public const int MaxPropertyCount = 256;

    /// <summary>Maximum categories accepted on one item.</summary>
    public const int MaxCategoriesPerItem = 4096;

    /// <summary>Maximum item ancestors accepted in one volume hierarchy.</summary>
    public const int MaxItemDepth = 256;

    /// <summary>How often the reader checks for cancellation while walking records.</summary>
    public const int CancelCheckEveryRecords = 500;
}
