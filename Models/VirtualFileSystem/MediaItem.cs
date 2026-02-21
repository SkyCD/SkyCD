using System.IO;

namespace SkyCD.Models.VirtualFileSystem
{
    public class MediaItem : FileSystemItem, IParentItem
    {
        // Use System.IO.DriveType instead of a custom MediaType enum.
        // Keep property name as MediaType to minimize changes elsewhere.
        public DriveType MediaType { get; set; } = DriveType.CDRom;

        // Use a generic collection constrained to IMediaChild so only FileItem/FolderItem are allowed.
        // The collection sets the Parent property of added children to this MediaItem.
        public MediaChildrenCollection<IMediaChild> Children { get; }

        public MediaItem()
        {
            Children = new MediaChildrenCollection<IMediaChild>(this);
        }
    }
}
