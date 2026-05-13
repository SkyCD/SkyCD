namespace SkyCD.Documents.Enum;

public enum CatalogDocumentType
{
    [DisplayName("File")]
    [CatalogDocumentTypeIconGlyph("file")]
    File = 0,

    [DisplayName("Media")]
    [CatalogDocumentTypeIconGlyph("video")]
    Media = 1,

    [DisplayName("Folder")]
    [CatalogDocumentTypeIconGlyph("folder")]
    Folder = 2,

    [DisplayName("Network Resource")]
    [CatalogDocumentTypeIconGlyph("network")]
    NetworkResource = 3
}