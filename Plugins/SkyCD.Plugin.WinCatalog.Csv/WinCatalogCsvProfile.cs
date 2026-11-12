using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Profiles and column header dictionaries for mapping localized WinCatalog CSV reports.
/// </summary>
public static class WinCatalogCsvProfile
{
    private static readonly Dictionary<string, WinCatalogCsvColumn> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Catalog Path (internal virtual tree path)
        ["Catalog Path"] = WinCatalogCsvColumn.CatalogPath,
        ["CatalogPath"] = WinCatalogCsvColumn.CatalogPath,
        ["Catalog_Path"] = WinCatalogCsvColumn.CatalogPath,
        ["Katalog-Pfad"] = WinCatalogCsvColumn.CatalogPath,
        ["Katalogpfad"] = WinCatalogCsvColumn.CatalogPath,
        ["Chemin du catalogue"] = WinCatalogCsvColumn.CatalogPath,
        ["Chemin catalogue"] = WinCatalogCsvColumn.CatalogPath,
        ["Ruta del catálogo"] = WinCatalogCsvColumn.CatalogPath,
        ["Ruta del catalogo"] = WinCatalogCsvColumn.CatalogPath,
        ["Ruta catalogo"] = WinCatalogCsvColumn.CatalogPath,
        ["Путь в каталоге"] = WinCatalogCsvColumn.CatalogPath,
        ["Percorso catalogo"] = WinCatalogCsvColumn.CatalogPath,

        // File Path (physical filesystem path)
        ["File Path"] = WinCatalogCsvColumn.FilePath,
        ["FilePath"] = WinCatalogCsvColumn.FilePath,
        ["File_Path"] = WinCatalogCsvColumn.FilePath,
        ["File System Path"] = WinCatalogCsvColumn.FilePath,
        ["FileSystemPath"] = WinCatalogCsvColumn.FilePath,
        ["Path"] = WinCatalogCsvColumn.FilePath,
        ["Dateipfad"] = WinCatalogCsvColumn.FilePath,
        ["Pfad"] = WinCatalogCsvColumn.FilePath,
        ["Chemin du fichier"] = WinCatalogCsvColumn.FilePath,
        ["Chemin fichier"] = WinCatalogCsvColumn.FilePath,
        ["Chemin"] = WinCatalogCsvColumn.FilePath,
        ["Ruta del archivo"] = WinCatalogCsvColumn.FilePath,
        ["Ruta archivo"] = WinCatalogCsvColumn.FilePath,
        ["Ruta"] = WinCatalogCsvColumn.FilePath,
        ["Путь к файлу"] = WinCatalogCsvColumn.FilePath,
        ["Путь"] = WinCatalogCsvColumn.FilePath,
        ["Percorso file"] = WinCatalogCsvColumn.FilePath,
        ["Percorso"] = WinCatalogCsvColumn.FilePath,

        // Name (display title)
        ["Name"] = WinCatalogCsvColumn.Name,
        ["Nom"] = WinCatalogCsvColumn.Name,
        ["Nombre"] = WinCatalogCsvColumn.Name,
        ["Имя"] = WinCatalogCsvColumn.Name,
        ["Nome"] = WinCatalogCsvColumn.Name,

        // Filename (filesystem name)
        ["Filename"] = WinCatalogCsvColumn.Filename,
        ["File Name"] = WinCatalogCsvColumn.Filename,
        ["Dateiname"] = WinCatalogCsvColumn.Filename,
        ["Nom de fichier"] = WinCatalogCsvColumn.Filename,
        ["Nombre de archivo"] = WinCatalogCsvColumn.Filename,
        ["Имя файла"] = WinCatalogCsvColumn.Filename,
        ["Nome file"] = WinCatalogCsvColumn.Filename,

        // Disk / Media
        ["Disk"] = WinCatalogCsvColumn.Disk,
        ["Disk Name"] = WinCatalogCsvColumn.Disk,
        ["Media"] = WinCatalogCsvColumn.Disk,
        ["Medium"] = WinCatalogCsvColumn.Disk,
        ["Datenträger"] = WinCatalogCsvColumn.Disk,
        ["Datentrager"] = WinCatalogCsvColumn.Disk,
        ["Disque"] = WinCatalogCsvColumn.Disk,
        ["Média"] = WinCatalogCsvColumn.Disk,
        ["Disco"] = WinCatalogCsvColumn.Disk,
        ["Диск"] = WinCatalogCsvColumn.Disk,
        ["Supporto"] = WinCatalogCsvColumn.Disk,

        // Volume Label
        ["Volume Label"] = WinCatalogCsvColumn.VolumeLabel,
        ["VolumeLabel"] = WinCatalogCsvColumn.VolumeLabel,
        ["Volume"] = WinCatalogCsvColumn.VolumeLabel,
        ["Datenträgerbezeichnung"] = WinCatalogCsvColumn.VolumeLabel,
        ["Datentragerbezeichnung"] = WinCatalogCsvColumn.VolumeLabel,
        ["Nom de volume"] = WinCatalogCsvColumn.VolumeLabel,
        ["Etiqueta de volumen"] = WinCatalogCsvColumn.VolumeLabel,
        ["Метка тома"] = WinCatalogCsvColumn.VolumeLabel,
        ["Etichetta volume"] = WinCatalogCsvColumn.VolumeLabel,

        // Size
        ["Size"] = WinCatalogCsvColumn.Size,
        ["File Size"] = WinCatalogCsvColumn.Size,
        ["FileSize"] = WinCatalogCsvColumn.Size,
        ["Größe"] = WinCatalogCsvColumn.Size,
        ["Grosse"] = WinCatalogCsvColumn.Size,
        ["Dateigröße"] = WinCatalogCsvColumn.Size,
        ["Dateigrosse"] = WinCatalogCsvColumn.Size,
        ["Taille"] = WinCatalogCsvColumn.Size,
        ["Tamaño"] = WinCatalogCsvColumn.Size,
        ["Tamano"] = WinCatalogCsvColumn.Size,
        ["Размер"] = WinCatalogCsvColumn.Size,
        ["Dimensione"] = WinCatalogCsvColumn.Size,

        // Type
        ["Type"] = WinCatalogCsvColumn.Type,
        ["Entry Type"] = WinCatalogCsvColumn.Type,
        ["Kind"] = WinCatalogCsvColumn.Type,
        ["Typ"] = WinCatalogCsvColumn.Type,
        ["Dateityp"] = WinCatalogCsvColumn.Type,
        ["Tipo"] = WinCatalogCsvColumn.Type,
        ["Тип"] = WinCatalogCsvColumn.Type,

        // Extension
        ["Ext"] = WinCatalogCsvColumn.Ext,
        ["Extension"] = WinCatalogCsvColumn.Ext,
        ["Erweiterung"] = WinCatalogCsvColumn.Ext,
        ["Dateiendung"] = WinCatalogCsvColumn.Ext,
        ["Extensión"] = WinCatalogCsvColumn.Ext,
        ["Extension de fichier"] = WinCatalogCsvColumn.Ext,
        ["Расширение"] = WinCatalogCsvColumn.Ext,
        ["Estensione"] = WinCatalogCsvColumn.Ext,

        // Created
        ["Created"] = WinCatalogCsvColumn.Created,
        ["Date Created"] = WinCatalogCsvColumn.Created,
        ["DateCreated"] = WinCatalogCsvColumn.Created,
        ["Erstellt"] = WinCatalogCsvColumn.Created,
        ["Erstellungsdatum"] = WinCatalogCsvColumn.Created,
        ["Créé"] = WinCatalogCsvColumn.Created,
        ["Cree"] = WinCatalogCsvColumn.Created,
        ["Date de création"] = WinCatalogCsvColumn.Created,
        ["Creado"] = WinCatalogCsvColumn.Created,
        ["Fecha de creación"] = WinCatalogCsvColumn.Created,
        ["Создан"] = WinCatalogCsvColumn.Created,
        ["Дата создания"] = WinCatalogCsvColumn.Created,
        ["Creato"] = WinCatalogCsvColumn.Created,
        ["Data creazione"] = WinCatalogCsvColumn.Created,

        // Modified
        ["Modified"] = WinCatalogCsvColumn.Modified,
        ["Date Modified"] = WinCatalogCsvColumn.Modified,
        ["DateModified"] = WinCatalogCsvColumn.Modified,
        ["Geändert"] = WinCatalogCsvColumn.Modified,
        ["Geandert"] = WinCatalogCsvColumn.Modified,
        ["Änderungsdatum"] = WinCatalogCsvColumn.Modified,
        ["Anderungsdatum"] = WinCatalogCsvColumn.Modified,
        ["Modifié"] = WinCatalogCsvColumn.Modified,
        ["Modifie"] = WinCatalogCsvColumn.Modified,
        ["Date de modification"] = WinCatalogCsvColumn.Modified,
        ["Modificado"] = WinCatalogCsvColumn.Modified,
        ["Fecha de modificación"] = WinCatalogCsvColumn.Modified,
        ["Изменен"] = WinCatalogCsvColumn.Modified,
        ["Дата изменения"] = WinCatalogCsvColumn.Modified,
        ["Modificato"] = WinCatalogCsvColumn.Modified,
        ["Data modifica"] = WinCatalogCsvColumn.Modified,

        // Comments
        ["Comments"] = WinCatalogCsvColumn.Comments,
        ["Comment"] = WinCatalogCsvColumn.Comments,
        ["Kommentare"] = WinCatalogCsvColumn.Comments,
        ["Kommentar"] = WinCatalogCsvColumn.Comments,
        ["Commentaires"] = WinCatalogCsvColumn.Comments,
        ["Commentaire"] = WinCatalogCsvColumn.Comments,
        ["Comentarios"] = WinCatalogCsvColumn.Comments,
        ["Comentario"] = WinCatalogCsvColumn.Comments,
        ["Комментарии"] = WinCatalogCsvColumn.Comments,
        ["Комментарий"] = WinCatalogCsvColumn.Comments,
        ["Commenti"] = WinCatalogCsvColumn.Comments,
        ["Commento"] = WinCatalogCsvColumn.Comments,

        // Tags
        ["Tags"] = WinCatalogCsvColumn.Tags,
        ["Tag"] = WinCatalogCsvColumn.Tags,
        ["Schlagwörter"] = WinCatalogCsvColumn.Tags,
        ["Schlagworter"] = WinCatalogCsvColumn.Tags,
        ["Balises"] = WinCatalogCsvColumn.Tags,
        ["Etiquetas"] = WinCatalogCsvColumn.Tags,
        ["Теги"] = WinCatalogCsvColumn.Tags,
        ["Метки"] = WinCatalogCsvColumn.Tags,

        // Rating
        ["Rating"] = WinCatalogCsvColumn.Rating,
        ["Bewertung"] = WinCatalogCsvColumn.Rating,
        ["Note"] = WinCatalogCsvColumn.Rating,
        ["Calificación"] = WinCatalogCsvColumn.Rating,
        ["Calificacion"] = WinCatalogCsvColumn.Rating,
        ["Рейтинг"] = WinCatalogCsvColumn.Rating,
        ["Valutazione"] = WinCatalogCsvColumn.Rating,

        // Checksums / Hashes
        ["MD5"] = WinCatalogCsvColumn.MD5,
        ["Prüfsumme MD5"] = WinCatalogCsvColumn.MD5,
        ["Prufsumme MD5"] = WinCatalogCsvColumn.MD5,
        ["SHA256"] = WinCatalogCsvColumn.SHA256,
        ["SHA-256"] = WinCatalogCsvColumn.SHA256,
        ["Blake2"] = WinCatalogCsvColumn.Blake2,
        ["CRC32"] = WinCatalogCsvColumn.CRC32,

        // Physical Location
        ["Physical Location"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Location"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Physischer Standort"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Standort"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Emplacement physique"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Emplacement"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Ubicación física"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Ubicacion fisica"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Местоположение"] = WinCatalogCsvColumn.PhysicalLocation,
        ["Posizione fisica"] = WinCatalogCsvColumn.PhysicalLocation
    };

    /// <summary>
    /// Maps a raw column header string to its semantic WinCatalog column.
    /// </summary>
    public static WinCatalogCsvColumn MapColumn(string header)
    {
        var trimmed = header.Trim().Trim('"', '\'');
        return ColumnAliases.TryGetValue(trimmed, out var column) ? column : WinCatalogCsvColumn.Unknown;
    }
}
