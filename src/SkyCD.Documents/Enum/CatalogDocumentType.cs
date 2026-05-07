namespace SkyCD.Documents.Enum;

public enum CatalogDocumentType
{
    [CatalogDocumentTypeIconGlyph("file")]
    File = 0,
    [CatalogDocumentTypeIconGlyph("video")]
    Media = 1,
    [CatalogDocumentTypeIconGlyph("folder")]
    Folder = 2,
    [CatalogDocumentTypeIconGlyph("network")]
    NetworkResource = 3
}
