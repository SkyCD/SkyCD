using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.NeoFinder.Text.Models;

/// <summary>
/// Header alias map for NeoFinder configurable text exports.
/// </summary>
/// <remarks>
/// NeoFinder always includes <c>Name</c> and lets the user pick the remaining
/// columns, the delimiter, the date format and whether headers are written, so
/// recognition is header-driven: a layout is claimed only when a <c>Name</c>
/// column plus at least one location column (path, volume or catalog) is
/// present. English headers below follow the export dialog and catalog
/// vocabulary documented in the NeoFinder users guide; localized variants are
/// best-effort and unmapped headers are reported as unsupported fields.
/// </remarks>
public static class NeoFinderTextProfile
{
    private static readonly Dictionary<string, NeoFinderTextColumn> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Name (always included by the exporter)
        ["Name"] = NeoFinderTextColumn.Name,
        ["File Name"] = NeoFinderTextColumn.Name,
        ["Filename"] = NeoFinderTextColumn.Name,
        ["Nom"] = NeoFinderTextColumn.Name,
        ["Nombre"] = NeoFinderTextColumn.Name,
        ["Nome"] = NeoFinderTextColumn.Name,
        ["Имя"] = NeoFinderTextColumn.Name,

        // Kind / media type
        ["Kind"] = NeoFinderTextColumn.Kind,
        ["Media Type"] = NeoFinderTextColumn.Kind,
        ["File Kind"] = NeoFinderTextColumn.Kind,
        ["Type"] = NeoFinderTextColumn.Kind,
        ["Art"] = NeoFinderTextColumn.Kind,
        ["Dateityp"] = NeoFinderTextColumn.Kind,
        ["Genre"] = NeoFinderTextColumn.Kind,
        ["Tipo"] = NeoFinderTextColumn.Kind,
        ["Clase"] = NeoFinderTextColumn.Kind,
        ["Tipo di file"] = NeoFinderTextColumn.Kind,
        ["Тип"] = NeoFinderTextColumn.Kind,
        ["Вид"] = NeoFinderTextColumn.Kind,

        // Size
        ["Size"] = NeoFinderTextColumn.Size,
        ["File Size"] = NeoFinderTextColumn.Size,
        ["Data Size"] = NeoFinderTextColumn.Size,
        ["Größe"] = NeoFinderTextColumn.Size,
        ["Dateigröße"] = NeoFinderTextColumn.Size,
        ["Taille"] = NeoFinderTextColumn.Size,
        ["Tamaño"] = NeoFinderTextColumn.Size,
        ["Dimensione"] = NeoFinderTextColumn.Size,
        ["Размер"] = NeoFinderTextColumn.Size,

        // Path / location on the volume
        ["Path"] = NeoFinderTextColumn.Path,
        ["Full Path"] = NeoFinderTextColumn.Path,
        ["Where"] = NeoFinderTextColumn.Path,
        ["Location"] = NeoFinderTextColumn.Path,
        ["Folder"] = NeoFinderTextColumn.Path,
        ["Folder Path"] = NeoFinderTextColumn.Path,
        ["Parent Folder"] = NeoFinderTextColumn.Path,
        ["Pfad"] = NeoFinderTextColumn.Path,
        ["Dateipfad"] = NeoFinderTextColumn.Path,
        ["Wo"] = NeoFinderTextColumn.Path,
        ["Speicherort"] = NeoFinderTextColumn.Path,
        ["Emplacement"] = NeoFinderTextColumn.Path,
        ["Où"] = NeoFinderTextColumn.Path,
        ["Ubicación"] = NeoFinderTextColumn.Path,
        ["Dónde"] = NeoFinderTextColumn.Path,
        ["Ruta"] = NeoFinderTextColumn.Path,
        ["Percorso"] = NeoFinderTextColumn.Path,
        ["Dove"] = NeoFinderTextColumn.Path,
        ["Путь"] = NeoFinderTextColumn.Path,
        ["Расположение"] = NeoFinderTextColumn.Path,

        // Volume the item was cataloged from
        ["Volume"] = NeoFinderTextColumn.Volume,
        ["Volume Name"] = NeoFinderTextColumn.Volume,
        ["Disk"] = NeoFinderTextColumn.Volume,
        ["Disk Name"] = NeoFinderTextColumn.Volume,
        ["Volumen"] = NeoFinderTextColumn.Volume,
        ["Volumenname"] = NeoFinderTextColumn.Volume,
        ["Datenträger"] = NeoFinderTextColumn.Volume,
        ["Disque"] = NeoFinderTextColumn.Volume,
        ["Nom du disque"] = NeoFinderTextColumn.Volume,
        ["Disco"] = NeoFinderTextColumn.Volume,
        ["Volume del disco"] = NeoFinderTextColumn.Volume,
        ["Том"] = NeoFinderTextColumn.Volume,
        ["Диск"] = NeoFinderTextColumn.Volume,

        // Catalog containing the item
        ["Catalog"] = NeoFinderTextColumn.Catalog,
        ["Catalog Name"] = NeoFinderTextColumn.Catalog,
        ["Katalog"] = NeoFinderTextColumn.Catalog,
        ["Katalogname"] = NeoFinderTextColumn.Catalog,
        ["Catalogue"] = NeoFinderTextColumn.Catalog,
        ["Nom du catalogue"] = NeoFinderTextColumn.Catalog,
        ["Catálogo"] = NeoFinderTextColumn.Catalog,
        ["Catalogo"] = NeoFinderTextColumn.Catalog,
        ["Каталог"] = NeoFinderTextColumn.Catalog,

        // Created
        ["Created"] = NeoFinderTextColumn.Created,
        ["Date Created"] = NeoFinderTextColumn.Created,
        ["Creation Date"] = NeoFinderTextColumn.Created,
        ["Erstellt"] = NeoFinderTextColumn.Created,
        ["Erstellungsdatum"] = NeoFinderTextColumn.Created,
        ["Créé"] = NeoFinderTextColumn.Created,
        ["Date de création"] = NeoFinderTextColumn.Created,
        ["Creado"] = NeoFinderTextColumn.Created,
        ["Fecha de creación"] = NeoFinderTextColumn.Created,
        ["Creato"] = NeoFinderTextColumn.Created,
        ["Data di creazione"] = NeoFinderTextColumn.Created,
        ["Создан"] = NeoFinderTextColumn.Created,
        ["Дата создания"] = NeoFinderTextColumn.Created,

        // Modified
        ["Modified"] = NeoFinderTextColumn.Modified,
        ["Date Modified"] = NeoFinderTextColumn.Modified,
        ["Modification Date"] = NeoFinderTextColumn.Modified,
        ["Last Modified"] = NeoFinderTextColumn.Modified,
        ["Geändert"] = NeoFinderTextColumn.Modified,
        ["Änderungsdatum"] = NeoFinderTextColumn.Modified,
        ["Modifié"] = NeoFinderTextColumn.Modified,
        ["Date de modification"] = NeoFinderTextColumn.Modified,
        ["Modificado"] = NeoFinderTextColumn.Modified,
        ["Fecha de modificación"] = NeoFinderTextColumn.Modified,
        ["Modificato"] = NeoFinderTextColumn.Modified,
        ["Data di modifica"] = NeoFinderTextColumn.Modified,
        ["Изменён"] = NeoFinderTextColumn.Modified,
        ["Изменен"] = NeoFinderTextColumn.Modified,
        ["Дата изменения"] = NeoFinderTextColumn.Modified,

        // Comments / annotations (includes NeoFinder custom fields)
        ["Comments"] = NeoFinderTextColumn.Comments,
        ["Comment"] = NeoFinderTextColumn.Comments,
        ["Annotation"] = NeoFinderTextColumn.Comments,
        ["Annotations"] = NeoFinderTextColumn.Comments,
        ["Description"] = NeoFinderTextColumn.Comments,
        ["Kommentar"] = NeoFinderTextColumn.Comments,
        ["Kommentare"] = NeoFinderTextColumn.Comments,
        ["Anmerkung"] = NeoFinderTextColumn.Comments,
        ["Commentaire"] = NeoFinderTextColumn.Comments,
        ["Commentaires"] = NeoFinderTextColumn.Comments,
        ["Comentario"] = NeoFinderTextColumn.Comments,
        ["Comentarios"] = NeoFinderTextColumn.Comments,
        ["Commento"] = NeoFinderTextColumn.Comments,
        ["Commenti"] = NeoFinderTextColumn.Comments,
        ["Комментарий"] = NeoFinderTextColumn.Comments,
        ["Комментарии"] = NeoFinderTextColumn.Comments,

        // Finder / NeoFinder label
        ["Label"] = NeoFinderTextColumn.Label,
        ["Color Label"] = NeoFinderTextColumn.Label,
        ["Etikett"] = NeoFinderTextColumn.Label,
        ["Farb Label"] = NeoFinderTextColumn.Label,
        ["Étiquette"] = NeoFinderTextColumn.Label,
        ["Etiqueta"] = NeoFinderTextColumn.Label,
        ["Etichetta"] = NeoFinderTextColumn.Label,
        ["Метка"] = NeoFinderTextColumn.Label,
    };

    /// <summary>
    /// Maps a raw header cell to its semantic column.
    /// </summary>
    public static NeoFinderTextColumn MapColumn(string header)
    {
        var trimmed = header.Trim().Trim('"', '\'');
        return ColumnAliases.TryGetValue(trimmed, out var column) ? column : NeoFinderTextColumn.Unknown;
    }
}
