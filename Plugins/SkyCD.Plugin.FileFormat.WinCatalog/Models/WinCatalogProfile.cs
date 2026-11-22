using System;
using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.WinCatalog.Models;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Models;

/// <summary>
/// Profiles and column header dictionaries for mapping localized WinCatalog report exports.
/// </summary>
public static class WinCatalogProfile
{
    private static readonly Dictionary<string, WinCatalogColumn> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Catalog Path (internal virtual tree path)
        ["Catalog Path"] = WinCatalogColumn.CatalogPath,
        ["CatalogPath"] = WinCatalogColumn.CatalogPath,
        ["Catalog_Path"] = WinCatalogColumn.CatalogPath,
        ["Katalog-Pfad"] = WinCatalogColumn.CatalogPath,
        ["Katalogpfad"] = WinCatalogColumn.CatalogPath,
        ["Chemin du catalogue"] = WinCatalogColumn.CatalogPath,
        ["Chemin catalogue"] = WinCatalogColumn.CatalogPath,
        ["Ruta del catálogo"] = WinCatalogColumn.CatalogPath,
        ["Ruta del catalogo"] = WinCatalogColumn.CatalogPath,
        ["Ruta catalogo"] = WinCatalogColumn.CatalogPath,
        ["Путь в каталоге"] = WinCatalogColumn.CatalogPath,
        ["Percorso catalogo"] = WinCatalogColumn.CatalogPath,

        // File Path (physical filesystem path)
        ["File Path"] = WinCatalogColumn.FilePath,
        ["FilePath"] = WinCatalogColumn.FilePath,
        ["File_Path"] = WinCatalogColumn.FilePath,
        ["File System Path"] = WinCatalogColumn.FilePath,
        ["FileSystemPath"] = WinCatalogColumn.FilePath,
        ["Path"] = WinCatalogColumn.FilePath,
        ["Dateipfad"] = WinCatalogColumn.FilePath,
        ["Pfad"] = WinCatalogColumn.FilePath,
        ["Chemin du fichier"] = WinCatalogColumn.FilePath,
        ["Chemin fichier"] = WinCatalogColumn.FilePath,
        ["Chemin"] = WinCatalogColumn.FilePath,
        ["Ruta del archivo"] = WinCatalogColumn.FilePath,
        ["Ruta archivo"] = WinCatalogColumn.FilePath,
        ["Ruta"] = WinCatalogColumn.FilePath,
        ["Путь к файлу"] = WinCatalogColumn.FilePath,
        ["Путь"] = WinCatalogColumn.FilePath,
        ["Percorso file"] = WinCatalogColumn.FilePath,
        ["Percorso"] = WinCatalogColumn.FilePath,

        // Name (display title)
        ["Name"] = WinCatalogColumn.Name,
        ["Nom"] = WinCatalogColumn.Name,
        ["Nombre"] = WinCatalogColumn.Name,
        ["Имя"] = WinCatalogColumn.Name,
        ["Nome"] = WinCatalogColumn.Name,

        // Filename (filesystem name)
        ["Filename"] = WinCatalogColumn.Filename,
        ["File Name"] = WinCatalogColumn.Filename,
        ["Dateiname"] = WinCatalogColumn.Filename,
        ["Nom de fichier"] = WinCatalogColumn.Filename,
        ["Nombre de archivo"] = WinCatalogColumn.Filename,
        ["Имя файла"] = WinCatalogColumn.Filename,
        ["Nome file"] = WinCatalogColumn.Filename,

        // Disk / Media
        ["Disk"] = WinCatalogColumn.Disk,
        ["Disk Name"] = WinCatalogColumn.Disk,
        ["Media"] = WinCatalogColumn.Disk,
        ["Medium"] = WinCatalogColumn.Disk,
        ["Datenträger"] = WinCatalogColumn.Disk,
        ["Datentrager"] = WinCatalogColumn.Disk,
        ["Disque"] = WinCatalogColumn.Disk,
        ["Média"] = WinCatalogColumn.Disk,
        ["Disco"] = WinCatalogColumn.Disk,
        ["Диск"] = WinCatalogColumn.Disk,
        ["Supporto"] = WinCatalogColumn.Disk,

        // Volume Label
        ["Volume Label"] = WinCatalogColumn.VolumeLabel,
        ["VolumeLabel"] = WinCatalogColumn.VolumeLabel,
        ["Volume"] = WinCatalogColumn.VolumeLabel,
        ["Datenträgerbezeichnung"] = WinCatalogColumn.VolumeLabel,
        ["Datentragerbezeichnung"] = WinCatalogColumn.VolumeLabel,
        ["Nom de volume"] = WinCatalogColumn.VolumeLabel,
        ["Etiqueta de volumen"] = WinCatalogColumn.VolumeLabel,
        ["Метка тома"] = WinCatalogColumn.VolumeLabel,
        ["Etichetta volume"] = WinCatalogColumn.VolumeLabel,

        // Size
        ["Size"] = WinCatalogColumn.Size,
        ["File Size"] = WinCatalogColumn.Size,
        ["FileSize"] = WinCatalogColumn.Size,
        ["Größe"] = WinCatalogColumn.Size,
        ["Grosse"] = WinCatalogColumn.Size,
        ["Dateigröße"] = WinCatalogColumn.Size,
        ["Dateigrosse"] = WinCatalogColumn.Size,
        ["Taille"] = WinCatalogColumn.Size,
        ["Tamaño"] = WinCatalogColumn.Size,
        ["Tamano"] = WinCatalogColumn.Size,
        ["Размер"] = WinCatalogColumn.Size,
        ["Dimensione"] = WinCatalogColumn.Size,

        // Type
        ["Type"] = WinCatalogColumn.Type,
        ["Entry Type"] = WinCatalogColumn.Type,
        ["Kind"] = WinCatalogColumn.Type,
        ["Typ"] = WinCatalogColumn.Type,
        ["Dateityp"] = WinCatalogColumn.Type,
        ["Tipo"] = WinCatalogColumn.Type,
        ["Тип"] = WinCatalogColumn.Type,

        // Extension
        ["Ext"] = WinCatalogColumn.Ext,
        ["Extension"] = WinCatalogColumn.Ext,
        ["Erweiterung"] = WinCatalogColumn.Ext,
        ["Dateiendung"] = WinCatalogColumn.Ext,
        ["Extensión"] = WinCatalogColumn.Ext,
        ["Extension de fichier"] = WinCatalogColumn.Ext,
        ["Расширение"] = WinCatalogColumn.Ext,
        ["Estensione"] = WinCatalogColumn.Ext,

        // Created
        ["Created"] = WinCatalogColumn.Created,
        ["Date Created"] = WinCatalogColumn.Created,
        ["DateCreated"] = WinCatalogColumn.Created,
        ["Erstellt"] = WinCatalogColumn.Created,
        ["Erstellungsdatum"] = WinCatalogColumn.Created,
        ["Créé"] = WinCatalogColumn.Created,
        ["Cree"] = WinCatalogColumn.Created,
        ["Date de création"] = WinCatalogColumn.Created,
        ["Creado"] = WinCatalogColumn.Created,
        ["Fecha de creación"] = WinCatalogColumn.Created,
        ["Создан"] = WinCatalogColumn.Created,
        ["Дата создания"] = WinCatalogColumn.Created,
        ["Creato"] = WinCatalogColumn.Created,
        ["Data creazione"] = WinCatalogColumn.Created,

        // Modified
        ["Modified"] = WinCatalogColumn.Modified,
        ["Date Modified"] = WinCatalogColumn.Modified,
        ["DateModified"] = WinCatalogColumn.Modified,
        ["Geändert"] = WinCatalogColumn.Modified,
        ["Geandert"] = WinCatalogColumn.Modified,
        ["Änderungsdatum"] = WinCatalogColumn.Modified,
        ["Anderungsdatum"] = WinCatalogColumn.Modified,
        ["Modifié"] = WinCatalogColumn.Modified,
        ["Modifie"] = WinCatalogColumn.Modified,
        ["Date de modification"] = WinCatalogColumn.Modified,
        ["Modificado"] = WinCatalogColumn.Modified,
        ["Fecha de modificación"] = WinCatalogColumn.Modified,
        ["Изменен"] = WinCatalogColumn.Modified,
        ["Дата изменения"] = WinCatalogColumn.Modified,
        ["Modificato"] = WinCatalogColumn.Modified,
        ["Data modifica"] = WinCatalogColumn.Modified,

        // Comments
        ["Comments"] = WinCatalogColumn.Comments,
        ["Comment"] = WinCatalogColumn.Comments,
        ["Kommentare"] = WinCatalogColumn.Comments,
        ["Kommentar"] = WinCatalogColumn.Comments,
        ["Commentaires"] = WinCatalogColumn.Comments,
        ["Commentaire"] = WinCatalogColumn.Comments,
        ["Comentarios"] = WinCatalogColumn.Comments,
        ["Comentario"] = WinCatalogColumn.Comments,
        ["Комментарии"] = WinCatalogColumn.Comments,
        ["Комментарий"] = WinCatalogColumn.Comments,
        ["Commenti"] = WinCatalogColumn.Comments,
        ["Commento"] = WinCatalogColumn.Comments,

        // Tags
        ["Tags"] = WinCatalogColumn.Tags,
        ["Tag"] = WinCatalogColumn.Tags,
        ["Schlagwörter"] = WinCatalogColumn.Tags,
        ["Schlagworter"] = WinCatalogColumn.Tags,
        ["Balises"] = WinCatalogColumn.Tags,
        ["Etiquetas"] = WinCatalogColumn.Tags,
        ["Теги"] = WinCatalogColumn.Tags,
        ["Метки"] = WinCatalogColumn.Tags,

        // Rating
        ["Rating"] = WinCatalogColumn.Rating,
        ["Bewertung"] = WinCatalogColumn.Rating,
        ["Note"] = WinCatalogColumn.Rating,
        ["Calificación"] = WinCatalogColumn.Rating,
        ["Calificacion"] = WinCatalogColumn.Rating,
        ["Рейтинг"] = WinCatalogColumn.Rating,
        ["Valutazione"] = WinCatalogColumn.Rating,

        // Checksums / Hashes
        ["MD5"] = WinCatalogColumn.MD5,
        ["Prüfsumme MD5"] = WinCatalogColumn.MD5,
        ["Prufsumme MD5"] = WinCatalogColumn.MD5,
        ["SHA256"] = WinCatalogColumn.SHA256,
        ["SHA-256"] = WinCatalogColumn.SHA256,
        ["Blake2"] = WinCatalogColumn.Blake2,
        ["CRC32"] = WinCatalogColumn.CRC32,

        // Physical Location
        ["Physical Location"] = WinCatalogColumn.PhysicalLocation,
        ["Location"] = WinCatalogColumn.PhysicalLocation,
        ["Physischer Standort"] = WinCatalogColumn.PhysicalLocation,
        ["Standort"] = WinCatalogColumn.PhysicalLocation,
        ["Emplacement physique"] = WinCatalogColumn.PhysicalLocation,
        ["Emplacement"] = WinCatalogColumn.PhysicalLocation,
        ["Ubicación física"] = WinCatalogColumn.PhysicalLocation,
        ["Ubicacion fisica"] = WinCatalogColumn.PhysicalLocation,
        ["Местоположение"] = WinCatalogColumn.PhysicalLocation,
        ["Posizione fisica"] = WinCatalogColumn.PhysicalLocation
    };

    /// <summary>
    /// Maps a raw column header string to its semantic WinCatalog column.
    /// </summary>
    public static WinCatalogColumn MapColumn(string header)
    {
        var trimmed = header.Trim().Trim('"', '\'');
        return ColumnAliases.TryGetValue(trimmed, out var column) ? column : WinCatalogColumn.Unknown;
    }
}
