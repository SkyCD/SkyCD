namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;

/// <summary>
/// Optional omissions applied when selected volumes are imported or exported.
/// Unknown payload kinds are always retained.
/// </summary>
public sealed record DedTransferOptions
{
    /// <summary>Gets whether stored file contents are copied.</summary>
    public bool IncludeContents { get; init; } = true;

    /// <summary>Gets whether thumbnails are copied.</summary>
    public bool IncludeThumbnails { get; init; } = true;

    /// <summary>Gets whether generated descriptions/previews are copied.</summary>
    public bool IncludeGeneratedDescriptions { get; init; } = true;

    /// <summary>Gets the defaults: everything is copied.</summary>
    public static DedTransferOptions All { get; } = new();

    internal bool Allows(DedPayloadKind kind) => kind switch
    {
        DedPayloadKind.StoredContent => IncludeContents,
        DedPayloadKind.Thumbnail => IncludeThumbnails,
        DedPayloadKind.GeneratedDescription => IncludeGeneratedDescriptions,
        _ => true
    };
}
