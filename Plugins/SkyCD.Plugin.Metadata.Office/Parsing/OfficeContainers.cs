using System;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// The stored form of a source, published as the <c>Container</c> field, and the leading bytes that identify it.
/// </summary>
internal static class OfficeContainers
{
    public const string Ooxml = "ooxml";
    public const string Ole = "ole";
    public const string OneNote = "onenote";
    public const string Cab = "cab";
    public const string Jet = "jet";

    /// <summary>Plain text, which is how Project's older interchange format is stored.</summary>
    public const string Text = "text";

    private static readonly byte[] PackageSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] CompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] CabinetSignature = "MSCF"u8.ToArray();
    private static readonly byte[] JetHeader = [0x00, 0x01, 0x00, 0x00];

    /// <summary>A stored (not empty or spanned) ZIP archive, which is how every OOXML package begins.</summary>
    public static bool IsPackage(ReadOnlySpan<byte> data) => data.StartsWith(PackageSignature);

    public static bool IsCompound(ReadOnlySpan<byte> data) => data.StartsWith(CompoundSignature);

    /// <summary>A cabinet, which is how a OneNote notebook package is stored.</summary>
    public static bool IsCabinet(ReadOnlySpan<byte> data) => data.StartsWith(CabinetSignature);

    /// <summary>A Jet database, whose first four bytes are a version word followed by the "Standard Jet DB" text.</summary>
    public static bool IsJet(ReadOnlySpan<byte> data) =>
        data.StartsWith(JetHeader) && data.Length > 0x20 && data[4] == (byte)'S';
}
