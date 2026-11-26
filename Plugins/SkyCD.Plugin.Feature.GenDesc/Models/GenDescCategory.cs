using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.Plugin.Feature.GenDesc.Models;

/// <summary>
/// Maps a file's MIME type and extension to the torrent/file-sharing category whose templates apply. The
/// extension wins first because archives and disk images carry generic MIME types; metadata facts can
/// refine an ambiguous video or audio file.
/// </summary>
public static class GenDescCategory
{
    public sealed record CategoryInfo(string Key, string Title);

    private static readonly CategoryInfo Movie = new("movie", "Movie");
    private static readonly CategoryInfo Tv = new("tv", "TV Show");
    private static readonly CategoryInfo Music = new("music", "Music");
    private static readonly CategoryInfo Audiobook = new("audiobook", "Audiobook");
    private static readonly CategoryInfo Ebook = new("ebook", "E-book");
    private static readonly CategoryInfo Comic = new("comic", "Comic");
    private static readonly CategoryInfo Software = new("software", "Software");
    private static readonly CategoryInfo Game = new("game", "Game");
    private static readonly CategoryInfo Rom = new("rom", "ROM");
    private static readonly CategoryInfo Image = new("image", "Image");
    private static readonly CategoryInfo Font = new("font", "Font");
    private static readonly CategoryInfo Course = new("course", "Course");
    private static readonly CategoryInfo Other = new("other", "Other");

    private static readonly Dictionary<string, CategoryInfo> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mkv"] = Movie,
        [".mp4"] = Movie,
        [".avi"] = Movie,
        [".mov"] = Movie,
        [".wmv"] = Movie,
        [".m2ts"] = Movie,
        [".webm"] = Movie,
        [".flv"] = Movie,
        [".mpg"] = Movie,
        [".mpeg"] = Movie,
        [".ts"] = Tv,
        [".m4v"] = Tv,
        [".flac"] = Music,
        [".mp3"] = Music,
        [".m4a"] = Music,
        [".aac"] = Music,
        [".ogg"] = Music,
        [".opus"] = Music,
        [".wav"] = Music,
        [".wma"] = Music,
        [".ape"] = Music,
        [".alac"] = Music,
        [".m4b"] = Audiobook,
        [".aa"] = Audiobook,
        [".aax"] = Audiobook,
        [".epub"] = Ebook,
        [".mobi"] = Ebook,
        [".azw"] = Ebook,
        [".azw3"] = Ebook,
        [".pdf"] = Ebook,
        [".djvu"] = Ebook,
        [".fb2"] = Ebook,
        [".lit"] = Ebook,
        [".cbz"] = Comic,
        [".cbr"] = Comic,
        [".cb7"] = Comic,
        [".cbt"] = Comic,
        [".exe"] = Software,
        [".msi"] = Software,
        [".dmg"] = Software,
        [".pkg"] = Software,
        [".deb"] = Software,
        [".rpm"] = Software,
        [".apk"] = Software,
        [".appimage"] = Software,
        [".iso"] = Game,
        [".nsp"] = Game,
        [".xci"] = Game,
        [".wbfs"] = Game,
        [".gcm"] = Game,
        [".nes"] = Rom,
        [".sfc"] = Rom,
        [".smc"] = Rom,
        [".gba"] = Rom,
        [".gb"] = Rom,
        [".gbc"] = Rom,
        [".nds"] = Rom,
        [".3ds"] = Rom,
        [".n64"] = Rom,
        [".z64"] = Rom,
        [".cue"] = Rom,
        [".bin"] = Rom,
        [".jpg"] = Image,
        [".jpeg"] = Image,
        [".png"] = Image,
        [".gif"] = Image,
        [".bmp"] = Image,
        [".webp"] = Image,
        [".tif"] = Image,
        [".tiff"] = Image,
        [".svg"] = Image,
        [".avif"] = Image,
        [".ttf"] = Font,
        [".otf"] = Font,
        [".woff"] = Font,
        [".woff2"] = Font,
        [".eot"] = Font
    };

    public static CategoryInfo Detect(string? extension, string? mimeType, IReadOnlyDictionary<string, string> facts)
    {
        if (Has(facts, "Season") || Has(facts, "Episode") || Has(facts, "EpisodeTitle")) return Tv;
        if (Has(facts, "Narrator") || Has(facts, "Audiobook")) return Audiobook;
        if (Has(facts, "Instructor") || Has(facts, "Course") || Has(facts, "Lessons")) return Course;

        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length > 0 && ByExtension.TryGetValue(normalizedExtension, out var byExtension))
        {
            if (byExtension == Music && Has(facts, "Narrator")) return Audiobook;
            return byExtension;
        }

        if (string.IsNullOrWhiteSpace(mimeType)) return Other;
        var mime = mimeType.Trim();
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return Movie;
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return Music;
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return Image;
        if (mime.StartsWith("font/", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("font", StringComparison.OrdinalIgnoreCase)) return Font;
        if (mime.Contains("comicbook", StringComparison.OrdinalIgnoreCase)) return Comic;
        if (mime.Contains("epub", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("mobipocket", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("pdf", StringComparison.OrdinalIgnoreCase)) return Ebook;
        if (mime.Contains("iso9660", StringComparison.OrdinalIgnoreCase)) return Game;
        if (mime.Contains("msdownload", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("msi", StringComparison.OrdinalIgnoreCase)) return Software;
        return Other;
    }

    /// <summary>The MIME type used by the CLI, which has no host MIME resolver.</summary>
    public static string MimeFromExtension(string? extension) => NormalizeExtension(extension) switch
    {
        ".mkv" => "video/x-matroska",
        ".mp4" or ".m4v" => "video/mp4",
        ".avi" => "video/x-msvideo",
        ".mov" => "video/quicktime",
        ".wmv" => "video/x-ms-wmv",
        ".webm" => "video/webm",
        ".ts" or ".m2ts" => "video/mp2t",
        ".flac" => "audio/flac",
        ".mp3" => "audio/mpeg",
        ".m4a" or ".m4b" => "audio/mp4",
        ".aac" => "audio/aac",
        ".ogg" or ".opus" => "audio/ogg",
        ".wav" => "audio/wav",
        ".wma" => "audio/x-ms-wma",
        ".epub" => "application/epub+zip",
        ".mobi" or ".azw" or ".azw3" => "application/x-mobipocket-ebook",
        ".pdf" => "application/pdf",
        ".cbz" => "application/vnd.comicbook+zip",
        ".cbr" => "application/vnd.comicbook-rar",
        ".exe" or ".msi" => "application/x-msdownload",
        ".dmg" => "application/x-apple-diskimage",
        ".deb" => "application/vnd.debian.binary-package",
        ".rpm" => "application/x-rpm",
        ".apk" => "application/vnd.android.package-archive",
        ".iso" => "application/x-iso9660-image",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".woff" or ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    private static bool Has(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        var value = extension.Trim();
        return (value.StartsWith('.') ? value : "." + value).ToLowerInvariant();
    }

    /// <summary>The category title used by templates as <c>CategoryTitle</c>.</summary>
    public static string TitleFor(string key) => key switch
    {
        "movie" => Movie.Title,
        "tv" => Tv.Title,
        "music" => Music.Title,
        "audiobook" => Audiobook.Title,
        "ebook" => Ebook.Title,
        "comic" => Comic.Title,
        "software" => Software.Title,
        "game" => Game.Title,
        "rom" => Rom.Title,
        "image" => Image.Title,
        "font" => Font.Title,
        "course" => Course.Title,
        _ => Other.Title
    };
}
