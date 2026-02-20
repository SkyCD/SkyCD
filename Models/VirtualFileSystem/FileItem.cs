namespace SkyCD.Models.VirtualFileSystem
{
    // FileItem represents a leaf (no children)
    public class FileItem : FileSystemItem, IMediaChild
    {
        // Reference to parent; prefer a typed reference instead of a raw int id.
        public IParentItem? Parent { get; set; } = null;
    }
}
