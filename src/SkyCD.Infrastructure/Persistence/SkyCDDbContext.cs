using Microsoft.EntityFrameworkCore;
using SkyCD.Domain.Catalogs;

namespace SkyCD.Infrastructure.Persistence;

public sealed class SkyCDDbContext(DbContextOptions<SkyCDDbContext> options) : DbContext(options)
{
    public DbSet<Catalog> Catalogs => Set<Catalog>();

    public DbSet<CatalogNode> CatalogNodes => Set<CatalogNode>();

    public DbSet<CatalogTag> CatalogTags => Set<CatalogTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Catalog>(entity =>
        {
            entity.ToTable("Catalogs");
            entity.HasKey(catalog => catalog.Id);
            entity.Property(catalog => catalog.Name).HasMaxLength(256).IsRequired();
            entity.Property(catalog => catalog.SchemaVersion).IsRequired();
            entity.Property(catalog => catalog.CreatedUtc).IsRequired();
            entity.Property(catalog => catalog.UpdatedUtc).IsRequired();
        });

        modelBuilder.Entity<CatalogNode>(entity =>
        {
            entity.ToTable("CatalogNodes");
            entity.HasKey(node => node.Id);
            entity.Property(node => node.Name).HasMaxLength(512).IsRequired();
            entity.Property(node => node.Kind).HasConversion<byte>().IsRequired();
            entity.Property(node => node.MimeType).HasMaxLength(256);
            entity.Property(node => node.MetadataJson);

            entity.HasOne(node => node.Catalog)
                .WithMany(catalog => catalog.Nodes)
                .HasForeignKey(node => node.CatalogId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(node => node.Parent)
                .WithMany(parent => parent.Children)
                .HasForeignKey(node => node.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(node => new { node.CatalogId, node.ParentId });
            entity.HasIndex(node => new { node.CatalogId, node.Kind });
            entity.HasIndex(node => new { node.CatalogId, node.ParentId, node.Name, node.Kind }).IsUnique();
        });

        modelBuilder.Entity<CatalogTag>(entity =>
        {
            entity.ToTable("CatalogTags");
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Name).HasMaxLength(128).IsRequired();
            entity.Property(tag => tag.Value).HasMaxLength(1024).IsRequired();

            entity.HasOne(tag => tag.Catalog)
                .WithMany(catalog => catalog.Tags)
                .HasForeignKey(tag => tag.CatalogId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(tag => new { tag.CatalogId, tag.Name });
        });
    }
}
