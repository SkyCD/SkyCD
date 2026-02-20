namespace SkyCD.Models.VirtualFileSystem
{
    // Common base type for files and folders
    public abstract class FileSystemItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public FileItemType Type { get; set; }
    }
}
