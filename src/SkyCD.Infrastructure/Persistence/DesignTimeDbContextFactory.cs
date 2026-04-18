using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SkyCD.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SkyCdDbContext>
{
    public SkyCdDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SkyCdDbContext>();
        optionsBuilder.UseSqlite("Data Source=skycd.v3.db");
        return new SkyCdDbContext(optionsBuilder.Options);
    }
}
