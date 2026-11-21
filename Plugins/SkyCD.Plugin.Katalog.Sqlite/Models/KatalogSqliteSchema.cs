using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Katalog.Sqlite.Models;

/// <summary>
/// Katalog File-mode SQLite schema contract, derived from the upstream Katalog
/// source (core/database.cpp table factories, core/collection.cpp version and
/// catalog/device/file queries, core/catalog.cpp device join). Tables and
/// columns outside the required sets are ignored; missing optional columns
/// are read as absent rather than failing the import.
/// </summary>
public static class KatalogSqliteSchema
{
    public const string DeviceTable = "device";
    public const string CatalogTable = "catalog";
    public const string StorageTable = "storage";
    public const string FileTable = "file";
    public const string FolderTable = "folder";
    public const string TagTable = "tag";
    public const string ParameterTable = "parameter";

    /// <summary>
    /// Tables that must exist for the database to be treated as a Katalog
    /// collection. Statistics, search history, backup mappings, and catalog
    /// filters are intentionally excluded: they are documented as unsupported
    /// rather than required.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredTables =
    [
        DeviceTable, CatalogTable, FileTable, FolderTable, TagTable, ParameterTable
    ];

    /// <summary>
    /// Earliest schema version with the File-mode layout this importer reads.
    /// Upstream migrations start at 2.6; older layouts are rejected.
    /// </summary>
    public static readonly Version MinimumSchemaVersion = new(2, 6);

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredColumns =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DeviceTable] = ["device_id", "device_parent_id", "device_name", "device_type",
                "device_external_id", "device_path"],
            [CatalogTable] = ["catalog_id", "catalog_file_path"],
            [FileTable] = ["file_catalog_id", "file_name", "file_folder_path", "file_size",
                "file_date_updated", "file_full_path"],
            [FolderTable] = ["folder_catalog_id", "folder_path"],
            [TagTable] = ["ID", "name", "path"],
            [ParameterTable] = ["parameter_name", "parameter_type", "parameter_value1"]
        };

    /// <summary>
    /// Optional file columns with a slot in the shared catalog model.
    /// Upstream-only columns without a model slot (<c>file_catalog</c> catalog
    /// name, <c>video_framerate</c>, <c>video_bitrate</c>) are skipped and
    /// documented instead of silently dropped defaults.
    /// </summary>
    public static readonly IReadOnlyList<string> OptionalFileColumns =
    [
        "file_extension", "file_type", "mime_type", "mime_verified",
        "type_mismatch", "image_width", "image_height", "image_orientation",
        "video_duration_seconds", "video_width", "video_height", "video_codec",
        "audio_duration_seconds", "audio_artist",
        "audio_album", "audio_title", "audio_genre", "audio_year", "audio_track_number",
        "audio_bitrate", "audio_sample_rate", "metadata_extended", "metadata_extraction_date",
        "checksum_sha256", "checksum_extraction_date"
    ];
}
