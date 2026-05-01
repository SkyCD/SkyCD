using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SkyCD.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SkyCDDbContext>
{
    public SkyCDDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SkyCDDbContext>();
        optionsBuilder.UseSqlite("Data Source=skycd.v3.db");
        return new SkyCDDbContext(optionsBuilder.Options);
    }
}
