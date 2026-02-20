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

    public class MediaItem : FolderItem
    {
        public MediaType MediaType { get; set; } = MediaType.CD;
    }
}
