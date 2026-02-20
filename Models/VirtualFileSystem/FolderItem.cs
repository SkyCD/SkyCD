namespace SkyCD.Models.VirtualFileSystem
{
    public class FolderItem : FileSystemItem, IMediaChild, IParentItem
    {
        // Use the same typed collection as MediaItem so only IMediaChild entries are allowed
        // and the Parent property of children is updated automatically.
        public MediaChildrenCollection<IMediaChild> Children { get; }

        // Reference to parent folder or media (nullable)
        public IParentItem? Parent { get; set; } = null;

        public FolderItem()
        {
            Children = new MediaChildrenCollection<IMediaChild>(this);
        }
    }
}
