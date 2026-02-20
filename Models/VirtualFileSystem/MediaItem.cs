namespace SkyCD.Models.VirtualFileSystem
{
    public enum MediaType : byte
    {
        CD,
        DVD,
        BluRay,
        FDD,
        HDD,
        FTP
    }

    public class MediaItem : FileSystemItem, IParentItem
    {
        public MediaType MediaType { get; set; } = MediaType.CD;

        // Use a generic collection constrained to IMediaChild so only FileItem/FolderItem are allowed.
        // The collection sets the Parent property of added children to this MediaItem.
        public MediaChildrenCollection<IMediaChild> Children { get; }

        public MediaItem()
        {
            Children = new MediaChildrenCollection<IMediaChild>(this);
        }
    }
}
