using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Cathy.Models;

/// <summary>
/// Cathy catalog header metadata.
/// </summary>
public sealed record CathyCatalogHeader(
    short Version,
    DateTime? CatalogDate,
    string? Device,
    string? Volume,
    string? Alias,
    string? VolumeName,
    string? SerialNumber,
    string? Comment,
    uint FreeSizeMb,
    short ArchiveFlag);

/// <summary>
/// Cathy folder index record from path directory block.
/// </summary>
public sealed record CathyFolderRecord(
    int FolderId,
    string Name,
    int FileCount,
    double TotalSize);

/// <summary>
/// Cathy entry in the file list.
/// If IsDirectory is true, DirectoryId represents the folder ID index.
/// </summary>
public sealed record CathyFileEntry(
    DateTime? ModifiedDate,
    long Size,
    int ParentFolderId,
    string Name,
    bool IsDirectory,
    int DirectoryId);

/// <summary>
/// Complete parsed Cathy catalog data model.
/// </summary>
public sealed record CathyCatalog(
    CathyCatalogHeader Header,
    IReadOnlyList<CathyFolderRecord> Folders,
    IReadOnlyList<CathyFileEntry> Entries);
