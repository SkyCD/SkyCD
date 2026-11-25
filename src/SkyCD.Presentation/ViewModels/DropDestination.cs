using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Catalog container that receives an OS-dropped source. Captured while the pointer is over the
/// window so the parent cannot drift between the drag and the confirmed Add To List dialog.
/// </summary>
public readonly record struct DropDestination(string ParentId, string? DisplayName)
{
    public const string LibraryRootId = "library";

    public static DropDestination LibraryRoot { get; } = new(LibraryRootId, null);

    public static DropDestination FromTreeNode(string key, string? title) => new(key, title);

    /// <summary>Returns a destination for container items only; files cannot hold children.</summary>
    public static DropDestination? FromCatalogItem(string id, string? displayName, CatalogDocumentType type) =>
        IsContainer(type) ? new(id, displayName) : null;

    public static bool IsContainer(CatalogDocumentType type) =>
        type is CatalogDocumentType.Media
            or CatalogDocumentType.Folder
            or CatalogDocumentType.NetworkFolder
            or CatalogDocumentType.NetworkResource;
}
