using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Vvv;

internal sealed record VvvBackupVolume(
    long Id,
    string? Name,
    string? Description,
    string? PhysicalPath,
    DateTime? CatalogDate,
    DateTime? LastUpdateDate);

internal sealed record VvvBackupPath(
    long Id,
    long VolumeId,
    string? Name,
    long? ParentId,
    string? Description);

internal sealed record VvvBackupFile(
    long Id,
    string? Name,
    long Size,
    string? Extension,
    DateTime? Modified,
    long? PathId,
    long? FolderPathId,
    string? Description);

internal sealed record VvvBackupAudio(
    string? Title,
    string? Artist,
    string? Album,
    int? Year,
    string? Comment,
    int? TrackNumber,
    string? Genre,
    int? Bitrate,
    int? SampleRate,
    short? Channels,
    int? LengthSeconds);

internal sealed record VvvBackupVirtualPath(
    long Id,
    string? Name,
    long? ParentId,
    long? PhysicalPathId);

internal sealed record VvvBackupVirtualFile(
    long FileId,
    long VirtualPathId,
    long? PhysicalFileId,
    long? VirtualPathFileId);

internal sealed class VvvBackupSnapshot
{
    internal int SchemaVersion { get; init; }

    internal List<VvvBackupVolume> Volumes { get; } = [];

    internal List<VvvBackupPath> Paths { get; } = [];

    internal List<VvvBackupFile> Files { get; } = [];

    internal Dictionary<long, VvvBackupAudio> AudioByFileId { get; } = [];

    internal List<VvvBackupVirtualPath> VirtualPaths { get; } = [];

    internal List<VvvBackupVirtualFile> VirtualFiles { get; } = [];
}
