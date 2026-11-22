namespace SkyCD.Plugin.Metadata.Vdi.Models;

/// <summary>Constants from the VirtualBox VDI image header.</summary>
internal static class VdiImageType
{
    /// <summary>Header version written by VirtualBox 1.2 and later, the only layout this reader accepts.</summary>
    public const uint Version1002 = 0x0001_0002;

    /// <summary>Static (fixed) image: guest data follows the image header directly.</summary>
    public const uint Fixed = 1;

    /// <summary>Dynamically allocated image: guest data is reached through the block map.</summary>
    public const uint Dynamic = 2;
}

/// <summary>Constants from the VirtualBox VDI image header flags.</summary>
internal static class VdiImageFlags
{
    /// <summary>Set when the image belongs to a snapshot chain and needs its base image.</summary>
    public const uint UsesBase = 0x0000_0001;

    /// <summary>Obsolete alias of <see cref="UsesBase"/> kept for headers written by old tools.</summary>
    public const uint UsesBaseSnapshotToo = 0x0000_0002;

    /// <summary>Set when the guest data is encrypted; the plugin cannot read such images.</summary>
    public const uint Encrypted = 0x0000_0004;
}

/// <summary>Constants from the VirtualBox VDI block map.</summary>
internal static class VdiBlock
{
    /// <summary>Block map value for a block that was never written; it reads back as zeros.</summary>
    public const uint Unallocated = 0xffff_ffff;
}
