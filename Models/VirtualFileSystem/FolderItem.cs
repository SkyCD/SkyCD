namespace SkyCD.Models.VirtualFileSystem
{
    public class FolderItem : FileSystemItem
    {
        public System.Collections.ObjectModel.ObservableCollection<FileSystemItem> Children { get; set; } = new();
    }
}
