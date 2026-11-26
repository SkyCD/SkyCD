using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkyCD.Plugin.Feature.GenDesc.Models;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>
/// The templates shipped with the plugin. The layout follows the old GenDesc.NET templates (a bold title,
/// guarded fact lines and BBCode), and every torrent/file-sharing category gets both an .nfo and a
/// description template so an entry of any kind renders without falling back to an unrelated layout.
/// </summary>
internal static class BuiltInTemplates
{
    private sealed record Field(string Key, string English, string Lithuanian, bool List = false, bool Paragraph = false);

    private sealed record Category(string Key, string Name, string Mime, string Extensions, Field[] Fields);

    private static readonly Category[] Categories =
    [
        new("movie", "Movie",
            "video/x-matroska video/mp4 video/x-msvideo video/quicktime video/x-ms-wmv video/webm",
            ".mkv .mp4 .avi .mov .wmv .m2ts .webm",
            [
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Runtime", "Runtime", "Trukmė"),
                new("Director", "Director", "Režisierius"),
                new("Cast", "Cast", "Vaidina", List: true),
                new("Country", "Country", "Šalis"),
                new("Language", "Language", "Kalba"),
                new("Rating", "Rating", "Reitingas"),
                new("Plot", "Plot", "Aprašymas", Paragraph: true)
            ]),
        new("tv", "TV Show",
            "video/x-matroska video/mp4 video/x-msvideo video/quicktime video/webm",
            ".mkv .mp4 .avi .mov .ts .m2ts .webm",
            [
                new("Season", "Season", "Sezonas"),
                new("Episode", "Episode", "Serija"),
                new("EpisodeTitle", "Episode title", "Serijos pavadinimas"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Network", "Network", "Kanalas"),
                new("Director", "Director", "Režisierius"),
                new("Cast", "Cast", "Vaidina", List: true),
                new("Rating", "Rating", "Reitingas"),
                new("Plot", "Plot", "Aprašymas", Paragraph: true)
            ]),
        new("music", "Music",
            "audio/*",
            ".flac .mp3 .m4a .aac .ogg .opus .wav .wma .ape .alac",
            [
                new("Artist", "Artist", "Atlikėjas"),
                new("Album", "Album", "Albumas"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Track", "Track", "Takelis"),
                new("Bitrate", "Bitrate", "Bitrate"),
                new("Format", "Format", "Formatas"),
                new("Label", "Label", "Leidykla"),
                new("Duration", "Duration", "Trukmė")
            ]),
        new("audiobook", "Audiobook",
            "audio/mp4 audio/mpeg audio/aac audio/ogg",
            ".m4b .m4a .aa .aax .mp3 .ogg",
            [
                new("Author", "Author", "Autorius"),
                new("Narrator", "Narrator", "Skaitovas"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Runtime", "Runtime", "Trukmė"),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Bitrate", "Bitrate", "Bitrate"),
                new("Format", "Format", "Formatas"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ]),
        new("game", "Game",
            "application/x-iso9660-image application/x-msdownload application/octet-stream",
            ".iso .nsp .xci .wbfs .gcm .exe .msi",
            [
                new("Platform", "Platform", "Platforma"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Developer", "Developer", "Kūrėjas"),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Version", "Version", "Versija"),
                new("Language", "Language", "Kalba"),
                new("Rating", "Rating", "Reitingas"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ]),
        new("software", "Software",
            "application/x-msdownload application/x-msi application/x-apple-diskimage application/vnd.debian.binary-package application/x-rpm application/vnd.android.package-archive",
            ".exe .msi .dmg .pkg .deb .rpm .apk .appimage",
            [
                new("Version", "Version", "Versija"),
                new("Platform", "Platform", "Platforma"),
                new("Developer", "Developer", "Kūrėjas"),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Year", "Year", "Metai"),
                new("License", "License", "Licencija"),
                new("Requirements", "Requirements", "Reikalavimai"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ]),
        new("ebook", "E-book",
            "application/epub+zip application/pdf application/x-mobipocket-ebook application/vnd.amazon.ebook",
            ".epub .mobi .azw .azw3 .pdf .djvu .fb2 .lit",
            [
                new("Author", "Author", "Autorius"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Language", "Language", "Kalba"),
                new("Pages", "Pages", "Puslapiai"),
                new("ISBN", "ISBN", "ISBN"),
                new("Format", "Format", "Formatas"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ]),
        new("comic", "Comic",
            "application/vnd.comicbook+zip application/vnd.comicbook-rar application/pdf image/*",
            ".cbz .cbr .cb7 .cbt .pdf",
            [
                new("Series", "Series", "Serija"),
                new("Issue", "Issue", "Numeris"),
                new("Author", "Writer", "Scenaristas"),
                new("Artist", "Artist", "Dailininkas"),
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Pages", "Pages", "Puslapiai"),
                new("Format", "Format", "Formatas")
            ]),
        new("course", "Course",
            "video/* application/pdf application/zip",
            ".mp4 .mkv .avi .pdf .zip .7z",
            [
                new("Instructor", "Instructor", "Dėstytojas"),
                new("Year", "Year", "Metai"),
                new("Duration", "Duration", "Trukmė"),
                new("Lessons", "Lessons", "Pamokos"),
                new("Topic", "Topic", "Tema", List: true),
                new("Language", "Language", "Kalba"),
                new("Format", "Format", "Formatas"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ]),
        new("image", "Image",
            "image/*",
            ".jpg .jpeg .png .gif .bmp .webp .tif .tiff .svg .avif",
            [
                new("Resolution", "Resolution", "Rezoliucija"),
                new("Format", "Format", "Formatas"),
                new("ColorDepth", "Color depth", "Spalvų gylis"),
                new("Author", "Author", "Autorius"),
                new("Year", "Year", "Metai"),
                new("Tags", "Tags", "Žymos", List: true)
            ]),
        new("font", "Font",
            "font/* application/font-sfnt application/font-woff",
            ".ttf .otf .woff .woff2 .eot",
            [
                new("Designer", "Designer", "Dizaineris"),
                new("Foundry", "Foundry", "Liejykla"),
                new("Year", "Year", "Metai"),
                new("Version", "Version", "Versija"),
                new("Format", "Format", "Formatas"),
                new("Language", "Languages", "Kalbos"),
                new("License", "License", "Licencija")
            ]),
        new("rom", "ROM",
            "application/octet-stream application/x-nes-rom application/x-gba-rom",
            ".nes .sfc .smc .gba .gb .gbc .nds .3ds .n64 .z64 .iso .bin",
            [
                new("Platform", "Platform", "Platforma"),
                new("Year", "Year", "Metai"),
                new("Region", "Region", "Regionas"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Developer", "Developer", "Kūrėjas"),
                new("Publisher", "Publisher", "Leidėjas"),
                new("Version", "Version", "Versija"),
                new("Language", "Language", "Kalba")
            ]),
        new("other", "Other", string.Empty, string.Empty,
            [
                new("Year", "Year", "Metai"),
                new("Genre", "Genre", "Žanras", List: true),
                new("Format", "Format", "Formatas"),
                new("Author", "Author", "Autorius"),
                new("Plot", "Description", "Aprašymas", Paragraph: true)
            ])
    ];

    public static IReadOnlyList<TemplateInfo> All()
    {
        var templates = new List<TemplateInfo>();
        foreach (var category in Categories)
        {
            templates.Add(Create(TemplateInfo.NfoKind, category, "en", lithuanian: false));
            templates.Add(Create(TemplateInfo.DescriptionKind, category, "en", lithuanian: false));
        }

        foreach (var key in new[] { "movie", "tv" })
        {
            var category = Categories.First(candidate => candidate.Key == key);
            templates.Add(Create(TemplateInfo.NfoKind, category, "lt", lithuanian: true));
            templates.Add(Create(TemplateInfo.DescriptionKind, category, "lt", lithuanian: true));
        }

        return templates;
    }

    private static TemplateInfo Create(string kind, Category category, string language, bool lithuanian)
    {
        var suffix = language == "en" ? string.Empty : "-" + language;
        return new TemplateInfo(
            $"default-{kind}-{category.Key}{suffix}",
            kind,
            category.Key,
            language,
            TemplateInfo.DefaultSite,
            "1",
            category.Mime,
            category.Extensions,
            BuildBody(kind, category, lithuanian));
    }

    private static string BuildBody(string kind, Category category, bool lithuanian)
    {
        var bbCode = !string.Equals(kind, TemplateInfo.NfoKind, StringComparison.Ordinal);
        var builder = new StringBuilder();
        builder.Append(bbCode ? "[b]" : string.Empty);
        builder.Append("<% Default(\"Title\", Name) %>");
        builder.Append("<% if Year %> (<% Year %>)<% end %>");
        builder.Append(bbCode ? "[/b]\n\n" : "\n" + new string('-', 60) + "\n\n");

        foreach (var field in category.Fields)
        {
            var label = lithuanian && !string.IsNullOrWhiteSpace(field.Lithuanian) ? field.Lithuanian : field.English;
            if (field.Paragraph) builder.Append('\n');
            AppendLine(builder, label, field.Key, field.List, bbCode);
        }

        if (!bbCode)
        {
            AppendFileFacts(builder, lithuanian);
        }

        return builder.ToString();
    }

    private static void AppendFileFacts(StringBuilder builder, bool lithuanian)
    {
        builder.Append('\n');
        AppendLine(builder, lithuanian ? "Byla" : "File", "FileName", list: false, bbCode: false);
        AppendLine(builder, lithuanian ? "Dydis" : "Size", "Size", list: false, bbCode: false);
        AppendLine(builder, lithuanian ? "Tipas" : "Type", "MimeType", list: false, bbCode: false);
        AppendLine(builder, lithuanian ? "Kategorija" : "Category", "CategoryTitle", list: false, bbCode: false);
    }

    private static void AppendLine(StringBuilder builder, string label, string key, bool list, bool bbCode)
    {
        // The newline lives inside the guard: a missing fact leaves no blank line behind, and a present one
        // ends its line so the next guard starts on a fresh line.
        builder.Append("<% if Has(\"").Append(key).Append("\") %>");
        builder.Append(bbCode ? "[i]" + label + ":[/i] " : label + ": ");
        builder.Append(list ? "<% Join(\"" + key + "\", \", \") %>" : "<% " + key + " %>");
        builder.Append("\n<% end %>");
    }
}
