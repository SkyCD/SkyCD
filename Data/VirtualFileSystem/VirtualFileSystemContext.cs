using Microsoft.EntityFrameworkCore;
using SkyCD.Models.VirtualFileSystem;

namespace SkyCD.Data.VirtualFileSystem
{
    public class VirtualFileSystemContext : DbContext
    {
        public VirtualFileSystemContext(DbContextOptions<VirtualFileSystemContext> options) : base(options) { }

        // Use the existing model types (FileSystemItem + derived types). EF will materialize the
        // correct CLR type (FolderItem/FileItem/MediaItem) using a discriminator mapped to the
        // existing `Type` property on FileSystemItem.
        public DbSet<FileSystemItem> FileSystemItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Map inheritance as TPH using the existing FileItemType enum property as discriminator.
            modelBuilder.Entity<FileSystemItem>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Name).IsRequired();
                b.Property(e => e.Type).IsRequired();

                // ParentId is not part of the domain model types (they use Parent references),
                // so map it as a shadow property. It will be present in the database and can be
                // accessed via EF.Property in queries.
                b.Property<int?>("ParentId");
            });

            // Configure discriminator to use the existing FileItemType enum value
            modelBuilder.Entity<FileSystemItem>()
                .HasDiscriminator<byte>(nameof(FileSystemItem.Type))
                .HasValue<FileItem>((byte)Models.FileItemType.File)
                .HasValue<FolderItem>((byte)Models.FileItemType.Folder)
                // If MediaItem is used as a top-level media container map it too (use FileItemType.Folder by default)
                .HasValue<MediaItem>((byte)Models.FileItemType.Folder);

            base.OnModelCreating(modelBuilder);
        }
    }
}
