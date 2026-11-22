using System;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// One scanned volume (disk, image, folder or network share) from a v5 catalog.
/// </summary>
public sealed class DiskCatalogMakerV5Volume
{
    public required uint VolumeId { get; init; }

    public required string Name { get; init; }

    public string? Source { get; init; }

    public DiskCatalogMakerV5VolumeKind Kind { get; init; }

    public DiskCatalogMakerV5FileSystem FileSystem { get; init; }

    public ulong CapacityBytes { get; init; }

    public ulong FreeBytes { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Scanned { get; init; }
}
