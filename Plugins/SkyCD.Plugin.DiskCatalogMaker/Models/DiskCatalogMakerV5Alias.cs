using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Models;

/// <summary>
/// Alias/original-item metadata for one v5 catalog item.
/// </summary>
public sealed class DiskCatalogMakerV5Alias
{
    public required uint AliasId { get; init; }

    public required uint ItemId { get; init; }

    public uint TargetVolumeId { get; init; }

    public uint TargetDirectoryId { get; init; }

    public string? MountPoint { get; init; }

    public string? PosixPath { get; init; }

    public string? Server { get; init; }

    public string? User { get; init; }

    public string? Zone { get; init; }

    public string? UnicodeName { get; init; }

    public string? UnicodeVolumeName { get; init; }

    public DateTimeOffset? VolumeCreated { get; init; }

    public DateTimeOffset? FileCreated { get; init; }

    public byte[]? RecursiveExtraBytes { get; init; }
}
