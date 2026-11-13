using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.WinCatalog.Csv;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Vocabulary for recognizing and classifying WinCatalog XML report elements.
/// Element and attribute names are matched case-insensitively and across the
/// localized WinCatalog UI languages (English, German, French, Spanish, Italian, Russian).
/// </summary>
public static class WinCatalogXmlVocabulary
{
    private static readonly string[] MediaNames =
    [
        "disk", "disc", "drive", "cd", "dvd", "bluray", "bd", "volume", "medium",
        "datenträger", "datentraeger", "disco", "disque", "диск", "том", "носитель"
    ];

    private static readonly string[] FolderNames =
    [
        "folder", "directory", "dir", "ordner", "dossier", "carpeta", "папка", "cartella"
    ];

    private static readonly string[] FileNames =
    [
        "file", "document", "item", "entry", "record", "datei", "fichier", "archivo", "файл", "documento"
    ];

    /// <summary>Elements that only group catalog entries and never become nodes themselves.</summary>
    private static readonly string[] TransparentNames =
    [
        "catalog", "catalogue", "wincatalog", "items", "results", "entries", "records", "data",
        "report", "reports", "disks", "discs", "drives", "folders", "files", "documents", "contents"
    ];

    private static readonly string[] GenericItemNames = ["item", "entry", "record"];

    private static readonly string[] MediaTypeWords =
    [
        "disk", "disc", "drive", "cd", "dvd", "bluray", "volume", "datenträger", "datentraeger", "disco", "disque", "диск", "том"
    ];

    /// <summary>
    /// Classifies an element by its local name only.
    /// </summary>
    public static WinCatalogXmlElementKind ClassifyByName(string localName)
    {
        var name = localName.Trim();
        if (TransparentNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return WinCatalogXmlElementKind.Transparent;
        }

        if (MediaNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return WinCatalogXmlElementKind.Media;
        }

        if (FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return WinCatalogXmlElementKind.Folder;
        }

        if (FileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return WinCatalogXmlElementKind.File;
        }

        return WinCatalogXmlElementKind.Transparent;
    }

    /// <summary>
    /// Classifies an element using its local name and, for generic item names,
    /// the exported type attribute value.
    /// </summary>
    public static WinCatalogXmlElementKind ClassifyElement(
        string localName,
        IReadOnlyDictionary<WinCatalogCsvColumn, string> fields)
    {
        var name = localName.Trim();
        var kind = ClassifyByName(name);
        if (kind != WinCatalogXmlElementKind.File ||
            !GenericItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return kind;
        }

        if (!fields.TryGetValue(WinCatalogCsvColumn.Type, out var typeText))
        {
            return kind;
        }

        if (IsFolderType(typeText))
        {
            return WinCatalogXmlElementKind.Folder;
        }

        if (IsMediaType(typeText))
        {
            return WinCatalogXmlElementKind.Media;
        }

        return kind;
    }

    public static bool IsFolderType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var lower = type.Trim().ToLowerInvariant();
        return lower is "folder" or "directory" or "ordner" or "dossier" or "carpeta" or "папка" or "cartella";
    }

    public static bool IsMediaType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var lower = type.Trim().ToLowerInvariant();
        return MediaTypeWords.Contains(lower, StringComparer.Ordinal);
    }
}
