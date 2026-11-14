using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// Parsed header and file entries from a Katalog .idx catalog file.
/// </summary>
public sealed class KatalogIdxCatalog
{
    public int CatalogId { get; set; }
    public string CatalogName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public long FileCount { get; set; }
    public long TotalFileSize { get; set; }
    public bool IncludeHidden { get; set; }
    public string FileType { get; set; } = string.Empty;
    public bool IncludeSymlinks { get; set; }
    public bool IncludeSubDir { get; set; }
    public bool IsFullDevice { get; set; }
    public string IncludeMetadata { get; set; } = string.Empty;
    public string IncludeChecksum { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public List<KatalogFileEntry> Files { get; } = [];
}

/// <summary>
/// A file entry record parsed from an .idx file.
/// </summary>
public sealed class KatalogFileEntry
{
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DateUpdated { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int? MimeVerified { get; set; }
    public int? TypeMismatch { get; set; }

    // Media metadata
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public int? ImageOrientation { get; set; }
    public double? VideoDurationSeconds { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public double? AudioDurationSeconds { get; set; }
    public string AudioArtist { get; set; } = string.Empty;
    public string AudioAlbum { get; set; } = string.Empty;
    public string AudioTitle { get; set; } = string.Empty;
    public string AudioGenre { get; set; } = string.Empty;
    public int? AudioYear { get; set; }
    public int? AudioTrackNumber { get; set; }
    public int? AudioBitrate { get; set; }
    public int? AudioSampleRate { get; set; }
    public string MetadataExtended { get; set; } = string.Empty;
    public string MetadataExtractionDate { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string ChecksumExtractionDate { get; set; } = string.Empty;
}
