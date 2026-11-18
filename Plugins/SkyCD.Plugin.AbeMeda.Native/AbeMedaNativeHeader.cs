namespace SkyCD.Plugin.AbeMeda.Native;

/// <summary>
/// Verified fixed header of a classic native catalog. Fields whose meaning is
/// not established are kept verbatim so later format work does not require a
/// re-import.
/// </summary>
public sealed record AbeMedaNativeHeader(
    uint Version,
    uint CompatibilityVersion,
    uint Integrity,
    uint Unknown2C,
    uint Unknown30,
    uint Unknown34,
    uint Unknown38,
    uint HeaderSize,
    uint EntryCount,
    uint EntryStride,
    uint SectionOffset,
    uint SectionLength,
    uint BlockSize);
