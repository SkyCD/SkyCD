using System;
using System.Collections.Generic;
using System.Globalization;

namespace SkyCD.Plugin.Basenji.Vdb;

/// <summary>
/// Extracts structured metadata properties from Basenji's serialized metadata format:
/// [type:length:type:length...]data
/// </summary>
public static class BasenjiMetadataExtractor
{
    private static readonly Dictionary<int, string> TypeToPropertyKey = new()
    {
        [1] = "FileName",
        [2] = "MimeType",
        [3] = "Title",
        [4] = "Author",
        [5] = "Artist",
        [6] = "Description",
        [7] = "Comment",
        [8] = "Date",
        [9] = "Publisher",
        [10] = "Language",
        [11] = "Album",
        [12] = "Genre",
        [13] = "Location",
        [14] = "VersionNumber",
        [15] = "Organization",
        [16] = "Copyright",
        [17] = "Subject",
        [18] = "Keywords",
        [19] = "Contributor",
        [20] = "ResourceType",
        [21] = "Format",
        [22] = "ResourceIdentifier",
        [23] = "Source",
        [24] = "Relation",
        [25] = "Coverage",
        [26] = "Software",
        [27] = "Disclaimer",
        [28] = "Warning",
        [29] = "Translated",
        [30] = "CreationDate",
        [31] = "ModificationDate",
        [32] = "Creator",
        [33] = "Producer",
        [34] = "PageCount",
        [35] = "PageOrientation",
        [36] = "PaperSize",
        [37] = "UsedFonts",
        [38] = "PageOrder",
        [39] = "CreatedFor",
        [40] = "Magnification",
        [41] = "Release",
        [42] = "Group",
        [43] = "Size",
        [44] = "Summary",
        [45] = "Packager",
        [46] = "Vendor",
        [47] = "License",
        [48] = "Distribution",
        [49] = "BuildHost",
        [50] = "Os",
        [51] = "Dependency",
        [52] = "HashMd4",
        [53] = "HashMd5",
        [54] = "HashSha0",
        [55] = "HashSha1",
        [56] = "HashRmd160",
        [57] = "Resolution",
        [58] = "Category",
        [59] = "BookTitle",
        [60] = "Priority",
        [61] = "Conflicts",
        [62] = "Replaces",
        [63] = "Provides",
        [64] = "Conductor",
        [65] = "Interpret",
        [66] = "Owner",
        [67] = "Lyrics",
        [68] = "MediaType",
        [69] = "Contact",
        [70] = "ThumbnailData",
        [71] = "PublicationDate",
        [72] = "CameraMake",
        [73] = "CameraModel",
        [74] = "Exposure",
        [75] = "Aperture",
        [76] = "ExposureBias",
        [77] = "Flash",
        [78] = "FlashBias",
        [79] = "FocalLength",
        [80] = "FocalLength35Mm",
        [81] = "IsoSpeed",
        [82] = "ExposureMode",
        [83] = "MeteringMode",
        [84] = "MacroMode",
        [85] = "ImageQuality",
        [86] = "WhiteBalance",
        [87] = "Orientation",
        [88] = "Template",
        [89] = "Split",
        [90] = "ProductVersion",
        [91] = "LastSavedBy",
        [92] = "LastPrinted",
        [93] = "WordCount",
        [94] = "CharacterCount",
        [95] = "TotalEditingTime",
        [96] = "Thumbnails",
        [97] = "Security",
        [98] = "CreatedBySoftware",
        [99] = "ModifiedBySoftware",
        [100] = "RevisionHistory",
        [101] = "Lowercase",
        [102] = "Company",
        [103] = "Generator",
        [104] = "CharacterSet",
        [105] = "LineCount",
        [106] = "ParagraphCount",
        [107] = "EditingCycles",
        [108] = "Scale",
        [109] = "Manager",
        [110] = "MovieDirector",
        [111] = "Duration",
        [112] = "Information",
        [113] = "FullName",
        [114] = "Chapter",
        [115] = "Year",
        [116] = "Link",
        [117] = "MusicCdIdentifier",
        [118] = "PlayCounter",
        [119] = "PopularityMeter",
        [120] = "ContentType",
        [121] = "EncodedBy",
        [122] = "Time",
        [123] = "MusicianCreditsList",
        [124] = "Mood",
        [125] = "FormatVersion",
        [126] = "TelevisionSystem",
        [127] = "SongCount",
        [128] = "StartingSong",
        [129] = "HardwareDependency",
        [130] = "Ripper",
        [131] = "FileSize",
        [132] = "TrackNumber",
        [133] = "Isrc",
        [134] = "DiscNumber",
        [135] = "GnunetDisplayType",
        [136] = "GnunetEcbcUri"
    };

    public static void ExtractMetadata(string? rawMetadata, IDictionary<string, object?> targetProperties)
    {
        if (string.IsNullOrWhiteSpace(rawMetadata))
        {
            return;
        }

        var trimmed = rawMetadata.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.Contains(']'))
        {
            return;
        }

        var headerEnd = trimmed.IndexOf(']');
        var header = trimmed[1..headerEnd];
        var data = trimmed[(headerEnd + 1)..];

        if (string.IsNullOrEmpty(header))
        {
            return;
        }

        var parts = header.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length % 2 != 0)
        {
            return;
        }

        var offset = 0;
        for (var i = 0; i < parts.Length; i += 2)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaType) ||
                !int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            {
                continue;
            }

            if (length <= 0 || offset + length > data.Length)
            {
                break;
            }

            var value = data.Substring(offset, length).Trim();
            offset += length;

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (TypeToPropertyKey.TryGetValue(metaType, out var key))
            {
                if (targetProperties.TryGetValue(key, out var existing) && existing is string existingStr && !string.IsNullOrWhiteSpace(existingStr))
                {
                    targetProperties[key] = $"{existingStr}; {value}";
                }
                else
                {
                    targetProperties[key] = value;
                }
            }
        }
    }
}
