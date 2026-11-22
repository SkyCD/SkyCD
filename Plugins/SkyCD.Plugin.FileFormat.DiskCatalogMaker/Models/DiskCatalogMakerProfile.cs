using System;
using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Profiles and column header dictionaries for mapping DiskCatalogMaker
/// text-export headers. Attribute order is user-configurable in DiskCatalogMaker,
/// so every supported attribute maps by header label instead of position.
/// </summary>
public static class DiskCatalogMakerProfile
{
    private static readonly Dictionary<string, DiskCatalogMakerColumn> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Path (full path; the "Path" attribute must be enabled in DiskCatalogMaker's list view)
        ["Path"] = DiskCatalogMakerColumn.Path,
        ["Full Path"] = DiskCatalogMakerColumn.Path,
        ["File Path"] = DiskCatalogMakerColumn.Path,
        ["Location"] = DiskCatalogMakerColumn.Path,
        ["Pfad"] = DiskCatalogMakerColumn.Path,
        ["Chemin"] = DiskCatalogMakerColumn.Path,
        ["Ruta"] = DiskCatalogMakerColumn.Path,
        ["パス"] = DiskCatalogMakerColumn.Path,
        ["場所"] = DiskCatalogMakerColumn.Path,

        // Name (always the first list-view column in DiskCatalogMaker)
        ["Name"] = DiskCatalogMakerColumn.Name,
        ["File Name"] = DiskCatalogMakerColumn.Name,
        ["Filename"] = DiskCatalogMakerColumn.Name,
        ["Dateiname"] = DiskCatalogMakerColumn.Name,
        ["Nom"] = DiskCatalogMakerColumn.Name,
        ["Nombre"] = DiskCatalogMakerColumn.Name,
        ["名前"] = DiskCatalogMakerColumn.Name,
        ["ファイル名"] = DiskCatalogMakerColumn.Name,

        // Size
        ["Size"] = DiskCatalogMakerColumn.Size,
        ["File Size"] = DiskCatalogMakerColumn.Size,
        ["Größe"] = DiskCatalogMakerColumn.Size,
        ["Grosse"] = DiskCatalogMakerColumn.Size,
        ["Taille"] = DiskCatalogMakerColumn.Size,
        ["Tamaño"] = DiskCatalogMakerColumn.Size,
        ["Tamano"] = DiskCatalogMakerColumn.Size,
        ["サイズ"] = DiskCatalogMakerColumn.Size,
        ["ファイルサイズ"] = DiskCatalogMakerColumn.Size,

        // Kind (the Finder kind string; DiskCatalogMaker's "Type" column carries the same meaning)
        ["Kind"] = DiskCatalogMakerColumn.Kind,
        ["Type"] = DiskCatalogMakerColumn.Kind,
        ["File Type"] = DiskCatalogMakerColumn.Kind,
        ["File Kind"] = DiskCatalogMakerColumn.Kind,
        ["Art"] = DiskCatalogMakerColumn.Kind,
        ["Genre"] = DiskCatalogMakerColumn.Kind,
        ["Tipo"] = DiskCatalogMakerColumn.Kind,
        ["種類"] = DiskCatalogMakerColumn.Kind,
        ["タイプ"] = DiskCatalogMakerColumn.Kind,

        // Date Modified
        ["Date Modified"] = DiskCatalogMakerColumn.DateModified,
        ["Modified"] = DiskCatalogMakerColumn.DateModified,
        ["Modification Date"] = DiskCatalogMakerColumn.DateModified,
        ["Last Modified"] = DiskCatalogMakerColumn.DateModified,
        ["Geändert"] = DiskCatalogMakerColumn.DateModified,
        ["Geandert"] = DiskCatalogMakerColumn.DateModified,
        ["Modifié"] = DiskCatalogMakerColumn.DateModified,
        ["Modifie"] = DiskCatalogMakerColumn.DateModified,
        ["Modificado"] = DiskCatalogMakerColumn.DateModified,
        ["更新日"] = DiskCatalogMakerColumn.DateModified,
        ["更新日時"] = DiskCatalogMakerColumn.DateModified,
        ["変更日"] = DiskCatalogMakerColumn.DateModified,

        // Date Created
        ["Date Created"] = DiskCatalogMakerColumn.DateCreated,
        ["Created"] = DiskCatalogMakerColumn.DateCreated,
        ["Creation Date"] = DiskCatalogMakerColumn.DateCreated,
        ["Erstellt"] = DiskCatalogMakerColumn.DateCreated,
        ["Créé"] = DiskCatalogMakerColumn.DateCreated,
        ["Cree"] = DiskCatalogMakerColumn.DateCreated,
        ["Creado"] = DiskCatalogMakerColumn.DateCreated,
        ["作成日"] = DiskCatalogMakerColumn.DateCreated,
        ["作成日時"] = DiskCatalogMakerColumn.DateCreated,

        // Creator (classic Mac OS creator code)
        ["Creator"] = DiskCatalogMakerColumn.Creator,
        ["Creator Code"] = DiskCatalogMakerColumn.Creator,
        ["Ersteller"] = DiskCatalogMakerColumn.Creator,
        ["Créateur"] = DiskCatalogMakerColumn.Creator,
        ["Createur"] = DiskCatalogMakerColumn.Creator,
        ["Creador"] = DiskCatalogMakerColumn.Creator,
        ["作成者"] = DiskCatalogMakerColumn.Creator,
        ["クリエータ"] = DiskCatalogMakerColumn.Creator,

        // Type code (classic Mac OS file type code; distinct from the Kind string)
        ["Type Code"] = DiskCatalogMakerColumn.TypeCode,
        ["TypeCode"] = DiskCatalogMakerColumn.TypeCode,
        ["File Type Code"] = DiskCatalogMakerColumn.TypeCode,
        ["ファイルタイプ"] = DiskCatalogMakerColumn.TypeCode,

        // Comments
        ["Comments"] = DiskCatalogMakerColumn.Comments,
        ["Comment"] = DiskCatalogMakerColumn.Comments,
        ["Kommentar"] = DiskCatalogMakerColumn.Comments,
        ["Kommentare"] = DiskCatalogMakerColumn.Comments,
        ["Commentaires"] = DiskCatalogMakerColumn.Comments,
        ["Commentaire"] = DiskCatalogMakerColumn.Comments,
        ["Comentarios"] = DiskCatalogMakerColumn.Comments,
        ["Comentario"] = DiskCatalogMakerColumn.Comments,
        ["コメント"] = DiskCatalogMakerColumn.Comments,
        ["備考"] = DiskCatalogMakerColumn.Comments,

        // Label (Finder label)
        ["Label"] = DiskCatalogMakerColumn.Label,
        ["Etikett"] = DiskCatalogMakerColumn.Label,
        ["Libellé"] = DiskCatalogMakerColumn.Label,
        ["Libelle"] = DiskCatalogMakerColumn.Label,
        ["Etiqueta"] = DiskCatalogMakerColumn.Label,
        ["ラベル"] = DiskCatalogMakerColumn.Label,

        // Disk / Volume (source media identity)
        ["Disk"] = DiskCatalogMakerColumn.Disk,
        ["Volume"] = DiskCatalogMakerColumn.Disk,
        ["Volume Name"] = DiskCatalogMakerColumn.Disk,
        ["Media"] = DiskCatalogMakerColumn.Disk,
        ["Datenträger"] = DiskCatalogMakerColumn.Disk,
        ["Datentrager"] = DiskCatalogMakerColumn.Disk,
        ["Disque"] = DiskCatalogMakerColumn.Disk,
        ["Disco"] = DiskCatalogMakerColumn.Disk,
        ["ディスク"] = DiskCatalogMakerColumn.Disk,
        ["ボリューム"] = DiskCatalogMakerColumn.Disk
    };

    /// <summary>
    /// Header labels that belong to other catalog tools and veto a DiskCatalogMaker match.
    /// DiskCatalogMaker has no checksum, tag, rating, catalog-path or Disk Explorer
    /// structural columns, so their presence proves a different export format.
    /// </summary>
    private static readonly HashSet<string> VetoHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Catalog Path",
        "CatalogPath",
        "FilePath",
        "Volume Label",
        "Physical Location",
        "MD5",
        "SHA1",
        "SHA256",
        "SHA-256",
        "Blake2",
        "CRC32",
        "Tags",
        "Rating",
        "Serial number",
        "NodeId",
        "ParentId",
        "SizeBytes",
        "ITEMTYPE",
        "FULLPATH",
        "CATEGORY",
        "DESC",
        "FILES",
        "FOLDERS"
    };

    /// <summary>
    /// Maps a raw column header string to its semantic DiskCatalogMaker column.
    /// </summary>
    public static DiskCatalogMakerColumn MapColumn(string header)
    {
        var trimmed = header.Trim().Trim('"', '\'');
        return ColumnAliases.TryGetValue(trimmed, out var column) ? column : DiskCatalogMakerColumn.Unknown;
    }

    /// <summary>
    /// Returns true when the header label proves the export came from another catalog tool.
    /// </summary>
    public static bool IsVetoHeader(string header)
    {
        return VetoHeaders.Contains(header.Trim().Trim('"', '\''));
    }
}
