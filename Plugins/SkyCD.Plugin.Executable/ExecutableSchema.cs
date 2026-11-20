using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Executable;

/// <summary>
/// Stable schema identity, field keys and source types published by the executable plugin. Field keys are
/// part of the cached-data contract: changing the meaning of a field requires a new schema version, because
/// the host keys stored results by schema id and version.
/// </summary>
public static class ExecutableSchema
{
    public const string MetadataSchemaId = "skycd.executable.metadata";
    public const int MetadataSchemaVersion = 1;

    /// <summary>Identity of the icon preview contract; it stores a PNG artifact rather than metadata values.</summary>
    public const string PreviewSchemaId = "skycd.executable.icon";
    public const int PreviewSchemaVersion = 1;

    /// <summary>Executable family the source belongs to, for example <c>pe</c> or <c>mz-16</c>.</summary>
    public const string Container = "Container";

    /// <summary>PE machine type, for example <c>x64</c>, <c>x86</c> or <c>arm64</c>.</summary>
    public const string Architecture = "Architecture";

    /// <summary>True when the image can be started as a program rather than only loaded as a library.</summary>
    public const string IsExecutableImage = "IsExecutableImage";

    public const string IsDotNet = "IsDotNet";
    public const string PeFormat = "PeFormat";
    public const string FileVersion = "FileVersion";
    public const string ProductVersion = "ProductVersion";
    public const string CompanyName = "CompanyName";
    public const string ProductName = "ProductName";
    public const string FileDescription = "FileDescription";
    public const string OriginalFilename = "OriginalFilename";
    public const string InternalName = "InternalName";
    public const string LegalCopyright = "LegalCopyright";
    public const string IconImageCount = "IconImageCount";
    public const string LargestIconWidth = "LargestIconWidth";
    public const string LargestIconHeight = "LargestIconHeight";

    public static readonly IReadOnlyList<string> Extensions = [".exe", ".dll", ".scr", ".com"];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/vnd.microsoft.portable-executable",
        "application/x-msdownload",
        "application/x-msdos-program",
        "application/x-dosexec"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Architecture, MediaMetadataType.Text),
        new MediaMetadataField(IsExecutableImage, MediaMetadataType.Boolean),
        new MediaMetadataField(IsDotNet, MediaMetadataType.Boolean),
        new MediaMetadataField(PeFormat, MediaMetadataType.Text),
        new MediaMetadataField(FileVersion, MediaMetadataType.Text),
        new MediaMetadataField(ProductVersion, MediaMetadataType.Text),
        new MediaMetadataField(CompanyName, MediaMetadataType.Text),
        new MediaMetadataField(ProductName, MediaMetadataType.Text),
        new MediaMetadataField(FileDescription, MediaMetadataType.Text),
        new MediaMetadataField(OriginalFilename, MediaMetadataType.Text),
        new MediaMetadataField(InternalName, MediaMetadataType.Text),
        new MediaMetadataField(LegalCopyright, MediaMetadataType.Text),
        new MediaMetadataField(IconImageCount, MediaMetadataType.Integer),
        new MediaMetadataField(LargestIconWidth, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(LargestIconHeight, MediaMetadataType.Integer, "px")
    ]);

    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
