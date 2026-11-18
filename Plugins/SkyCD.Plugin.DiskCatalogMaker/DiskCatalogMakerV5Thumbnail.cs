namespace SkyCD.Plugin.DiskCatalogMaker;

/// <summary>
/// Thumbnail reference from the v5 thumbnail table.
/// </summary>
public sealed class DiskCatalogMakerV5Thumbnail
{
    public required uint Key { get; init; }

    public uint ItemId { get; init; }

    public string? Extension { get; init; }
}
