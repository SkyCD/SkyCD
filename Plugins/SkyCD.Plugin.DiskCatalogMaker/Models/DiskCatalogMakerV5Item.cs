using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Models;

/// <summary>
/// One item (directory, file, package, alias or archive member) from a v5 catalog.
/// </summary>
public sealed class DiskCatalogMakerV5Item
{
    public required uint VolumeId { get; init; }

    public required uint ItemId { get; init; }

    public uint? ParentItemId { get; init; }

    public required string Name { get; init; }

    public string? FullPath { get; init; }

    public string? Comment { get; init; }

    public uint? AliasTargetItemId { get; init; }

    public uint? ThumbnailKey { get; init; }

    public ulong SizeBytes { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Modified { get; init; }

    public DateTimeOffset? Scanned { get; init; }

    public string? TypeCode { get; init; }

    public string? CreatorCode { get; init; }

    public DiskCatalogMakerV5ItemKind Kind { get; init; }

    public bool HasLabel { get; init; }

    public int Label { get; init; }

    public int? PosixMode { get; init; }
}
