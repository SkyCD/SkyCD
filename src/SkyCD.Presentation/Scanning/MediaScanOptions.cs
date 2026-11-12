using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Scanning;

public sealed record MediaScanOptions(
    string DrivePathOrName,
    string? MediaName = null,
    string? TargetParentId = null,
    CatalogDocumentType RootType = CatalogDocumentType.Media,
    bool IncludeMediaInfo = true,
    bool IncludeExtendedInfo = false,
    bool IncludeSubfolders = true);
