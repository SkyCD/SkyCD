namespace SkyCD.Plugin.DiskCatalogMaker;

/// <summary>
/// Semantic item kinds stored in the v5 volume directory table.
/// </summary>
public enum DiskCatalogMakerV5ItemKind
{
    Directory = 0,
    File = 1,
    Package = 2,
    Alias = 3,
    ArchiveMember = 4
}
