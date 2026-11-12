using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Scanning;

public sealed record FolderScanOptions(
    string FolderPath,
    string? MediaName = null,
    string? TargetParentId = "library",
    CatalogDocumentType RootType = CatalogDocumentType.Folder,
    bool IncludeSubfolders = true,
    bool IncludeExtendedInfo = false,
    ScanTraversalOptions? Traversal = null);
