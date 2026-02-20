using Microsoft.EntityFrameworkCore;
using SkyCD.Data.VirtualFileSystem;

namespace SkyCD.Services
{
    public static class DbMigrationRunner
    {
        public static void EnsureMigrated(string connectionString = "Data Source=virtualfs.db")
        {
            var options = new DbContextOptionsBuilder<VirtualFileSystemContext>()
                .UseSqlite(connectionString)
                .Options;

            using var db = new VirtualFileSystemContext(options);
            db.Database.Migrate();
        }
    }
}
