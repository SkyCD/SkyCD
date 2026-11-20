using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Documents;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Resolves a catalog entry to a MIME type for AI scan exclusion decisions. The entry's stored
/// <c>ContentType</c> property wins; otherwise a curated extension map is used. Unknown types resolve
/// to null and are never excluded by MIME type.
/// </summary>
public static class FileMimeTypeResolver
{
    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Executables and installers.
        [".exe"] = "application/x-msdownload",
        [".dll"] = "application/x-msdownload",
        [".com"] = "application/x-msdownload",
        [".ocx"] = "application/x-msdownload",
        [".sys"] = "application/x-msdownload",
        [".scr"] = "application/x-msdownload",
        [".msi"] = "application/x-msdownload",
        [".cpl"] = "application/x-msdownload",
        [".so"] = "application/x-sharedlib",
        [".dylib"] = "application/x-mach-binary",
        [".apk"] = "application/vnd.android.package-archive",
        [".deb"] = "application/vnd.debian.binary-package",
        [".rpm"] = "application/x-rpm",

        // Archives and disk images.
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".tgz"] = "application/gzip",
        [".bz2"] = "application/x-bzip2",
        [".tbz2"] = "application/x-bzip2",
        [".xz"] = "application/x-xz",
        [".txz"] = "application/x-xz",
        [".lzma"] = "application/x-lzma",
        [".zst"] = "application/zstd",
        [".tzst"] = "application/zstd",
        [".cab"] = "application/vnd.ms-cab-compressed",
        [".jar"] = "application/java-archive",
        [".war"] = "application/java-archive",
        [".ear"] = "application/java-archive",
        [".iso"] = "application/x-iso9660-image",
        [".dmg"] = "application/x-apple-diskimage",

        // Images.
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".ico"] = "image/vnd.microsoft.icon",
        [".heic"] = "image/heic",
        [".avif"] = "image/avif",

        // Audio.
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".wma"] = "audio/x-ms-wma",
        [".opus"] = "audio/opus",
        [".aif"] = "audio/aiff",
        [".aiff"] = "audio/aiff",
        [".mid"] = "audio/midi",
        [".midi"] = "audio/midi",

        // Video.
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".webm"] = "video/webm",
        [".flv"] = "video/x-flv",
        [".mpeg"] = "video/mpeg",
        [".mpg"] = "video/mpeg",
        [".3gp"] = "video/3gpp",

        // Text and documents.
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".odp"] = "application/vnd.oasis.opendocument.presentation",
        [".rtf"] = "application/rtf",
        [".epub"] = "application/epub+zip",

        // Fonts.
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2"
    };

    /// <summary>Resolves the MIME type of a catalog entry from its stored content type or file extension.</summary>
    public static string? Resolve(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Properties.TryGetValue("ContentType", out var value) &&
            value?.ToString() is { Length: > 0 } contentType)
        {
            return contentType.Trim();
        }

        return FromExtension(Path.GetExtension(entry.Name));
    }

    /// <summary>Resolves the MIME type registered for a file extension (with or without a leading dot).</summary>
    public static string? FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension[0] == '.' ? extension : "." + extension;
        return MimeTypesByExtension.TryGetValue(normalized, out var mimeType) ? mimeType : null;
    }
}
