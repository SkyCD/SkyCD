using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SkyCD.Data.VirtualFileSystem;

#nullable disable

namespace SkyCD.Migrations
{
    [DbContext(typeof(VirtualFileSystemContext))]
    partial class VirtualFileSystemModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.6");

            modelBuilder.Entity("SkyCD.Models.VirtualFileSystem.FileSystemItem", b =>
            {
                b.Property<int>("Id");

                b.Property<string>("Name")
                    .IsRequired();

                b.Property<byte>("Type");

                b.Property<int?>("ParentId");

                b.HasKey("Id");

                b.ToTable("FileSystemItems");
            });

            modelBuilder.Entity("SkyCD.Models.VirtualFileSystem.FileSystemItem", b =>
            {
                b.HasOne("SkyCD.Models.VirtualFileSystem.FileSystemItem")
                    .WithMany()
                    .HasForeignKey("ParentId");
            });

            modelBuilder.Entity("SkyCD.Models.VirtualFileSystem.FileItem", b =>
            {
                b.HasBaseType("SkyCD.Models.VirtualFileSystem.FileSystemItem");
                b.ToTable("FileSystemItems");
            });

            modelBuilder.Entity("SkyCD.Models.VirtualFileSystem.FolderItem", b =>
            {
                b.HasBaseType("SkyCD.Models.VirtualFileSystem.FileSystemItem");
                b.ToTable("FileSystemItems");
            });

            modelBuilder.Entity("SkyCD.Models.VirtualFileSystem.MediaItem", b =>
            {
                b.HasBaseType("SkyCD.Models.VirtualFileSystem.FileSystemItem");
                b.Property<byte>("MediaType");
                b.ToTable("FileSystemItems");
            });
        }
    }
}
