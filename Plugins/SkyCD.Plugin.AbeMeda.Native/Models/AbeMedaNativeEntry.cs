namespace SkyCD.Plugin.AbeMeda.Native.Models;

/// <summary>
/// One 36-byte record from the documented table. The blob value and the two
/// ranges are retained as observed because the format notes do not yet establish
/// whether they are offsets, sizes, or artifact ranges.
/// </summary>
public sealed record AbeMedaNativeEntry(
    long RecordOffset,
    ulong Ordinal,
    ulong BlobValue,
    uint Flags,
    string Handler,
    bool HandlerIsPrintable,
    uint RangeA,
    uint RangeB,
    bool RangesWithinFile);
