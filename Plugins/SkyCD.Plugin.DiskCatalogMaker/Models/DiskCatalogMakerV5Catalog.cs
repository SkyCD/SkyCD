using System.Collections.Generic;

namespace SkyCD.Plugin.DiskCatalogMaker.Models;

/// <summary>
/// Parsed representation of a DiskCatalogMaker v5 catalog payload.
/// </summary>
public sealed class DiskCatalogMakerV5Catalog
{
    public uint Revision { get; init; }

    public IReadOnlyList<DiskCatalogMakerV5Volume> Volumes { get; init; } = [];

    public IReadOnlyList<DiskCatalogMakerV5Item> Items { get; init; } = [];

    public IReadOnlyList<DiskCatalogMakerV5Alias> Aliases { get; init; } = [];

    public IReadOnlyList<DiskCatalogMakerV5Thumbnail> Thumbnails { get; init; } = [];

    public IReadOnlyList<DiskCatalogMakerV5UnknownTable> UnknownTables { get; init; } = [];

    public IReadOnlyList<DiskCatalogMakerV5TableInfo> Tables { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
