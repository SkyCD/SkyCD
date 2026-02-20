using Microsoft.EntityFrameworkCore;
using SkyCD.Data.VirtualFileSystem;
using SkyCD.Models.VirtualFileSystem;
using System.Collections.ObjectModel;
using System.Linq;

namespace SkyCD.Services
{
    public class VirtualFileSystemService
    {
        private readonly VirtualFileSystemContext _db;

        public VirtualFileSystemService(VirtualFileSystemContext db)
        {
            _db = db;
        }

        public ObservableCollection<FolderItem> LoadFolders()
        {
            // Load all items and use EF to materialize into the existing model types
            var items = _db.FileSystemItems.AsNoTracking().ToList();

            // Build lookup of id -> model instance (they are already of correct CLR type)
            var models = items.ToDictionary(i => i.Id);

            // Read shadow ParentId using EF.Property
            foreach (var item in items)
            {
                var parentId = Microsoft.EntityFrameworkCore.EF.Property<int?>(item, "ParentId");
                if (parentId != null && models.TryGetValue(parentId.Value, out var parent))
                {
                    if (parent is FolderItem folder && item is IMediaChild child)
                    {
                        folder.Children.Add(child);
                    }
                }
            }

            // Root folders are those with null ParentId and that are FolderItem
            var roots = items.Where(i => Microsoft.EntityFrameworkCore.EF.Property<int?>(i, "ParentId") == null)
                             .OfType<FolderItem>()
                             .ToList();

            return new ObservableCollection<FolderItem>(roots);
        }
    }
}
