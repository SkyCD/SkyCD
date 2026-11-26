using System;
using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Notebook.Models;

/// <summary>
/// Stable schema identity and field keys for Jupyter notebooks. Keys are part of the cached-data contract:
/// changing a field's meaning requires a new schema version.
/// </summary>
public static class NotebookSchema
{
    public const string MetadataSchemaId = "skycd.notebook.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string NotebookFormat = "NotebookFormat";
    public const string NbFormatVersion = "NbFormatVersion";
    public const string Language = "Language";
    public const string CellCount = "CellCount";
    public const string IncludedCellCount = "IncludedCellCount";
    public const string CellTitles = "CellTitles";

    public const string Valid = "Valid";
    public const string ErrorCode = "ErrorCode";
    public const string ErrorLine = "ErrorLine";
    public const string ErrorColumn = "ErrorColumn";
    public const string ByteLength = "ByteLength";

    public static readonly IReadOnlyList<string> Extensions = [".ipynb"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/x-ipynb+json"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(NotebookFormat, MediaMetadataType.Text),
        new MediaMetadataField(NbFormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(CellCount, MediaMetadataType.Integer),
        new MediaMetadataField(IncludedCellCount, MediaMetadataType.Integer),
        new MediaMetadataField(CellTitles, MediaMetadataType.Text),
        new MediaMetadataField(Valid, MediaMetadataType.Boolean),
        new MediaMetadataField(ErrorCode, MediaMetadataType.Text),
        new MediaMetadataField(ErrorLine, MediaMetadataType.Integer),
        new MediaMetadataField(ErrorColumn, MediaMetadataType.Integer),
        new MediaMetadataField(ByteLength, MediaMetadataType.Integer)
    ]);
}
