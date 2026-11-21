namespace SkyCD.Plugin.AbeMeda.Native.Models;

/// <summary>
/// Fixed offsets and limits for the documented classic abeMeda/NeoFinder native
/// catalog container. Only byte ranges verified against the shipped 7.8.7 demo
/// catalog are treated as known; everything else is preserved as raw metadata.
/// </summary>
public static class AbeMedaNativeLayout
{
    public const string SignatureText = "DWDRDNBA";
    public const int SignatureLength = 8;

    public const int VersionOffset = 0x08;
    public const int IntegrityOffset = 0x18;
    public const int CompatibilityVersionOffset = 0x20;
    public const int Unknown2COffset = 0x2C;
    public const int Unknown30Offset = 0x30;
    public const int Unknown34Offset = 0x34;
    public const int Unknown38Offset = 0x38;
    public const int SentinelOffset = 0x3C;
    public const int HeaderSizeOffset = 0x40;
    public const int EntryCountOffset = 0x44;
    public const int EntryStrideOffset = 0x48;
    public const int SectionOffsetOffset = 0x4C;
    public const int SectionLengthOffset = 0x50;
    public const int BlockSizeOffset = 0x64;

    /// <summary>Minimum bytes needed before the fixed header fields can be read.</summary>
    public const int MinimumHeaderBytes = 0x68;

    public const uint SentinelValue = 0xA5A5A5A5;

    /// <summary>Format marker observed at both version offsets in the 7.8.7 line.</summary>
    public const uint SupportedVersion = 105;

    public const uint SupportedHeaderSize = 0x90;
    public const uint SupportedEntryStride = 36;

    public const int MaxEntries = 1_000_000;
    public const uint MaxBlockSize = 16 * 1024 * 1024;

    public const int HandlerLength = 8;

    public const int EntryOrdinalOffset = 0;
    public const int EntryBlobValueOffset = 8;
    public const int EntryFlagsOffset = 16;
    public const int EntryHandlerOffset = 20;
    public const int EntryRangeAOffset = 28;
    public const int EntryRangeBOffset = 32;
}
