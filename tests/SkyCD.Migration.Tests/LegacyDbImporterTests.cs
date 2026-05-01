using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SkyCD.Infrastructure.Persistence;
using SkyCD.Migration.Cli;

namespace SkyCD.Migration.Tests;

public sealed class LegacyDbImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skycd-migration-{Guid.NewGuid():N}");

    public LegacyDbImporterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ImportAsync_ImportsLegacyRowsIntoTargetSchema()
    {
        var legacyPath = Path.Combine(_root, "legacy.db");
        var targetPath = Path.Combine(_root, "target.db");

        await SeedLegacyDatabaseAsync(legacyPath);

        var importer = new LegacyDbImporter();
        var result = await importer.ImportAsync(legacyPath, targetPath, dryRun: false);

        Assert.Equal(1, result.ImportedCatalogs);
        Assert.Equal(2, result.ImportedNodes);
        Assert.Empty(result.Errors);

        await using var context = new SkyCDDbContext(
            new DbContextOptionsBuilder<SkyCDDbContext>()
                .UseSqlite($"Data Source={targetPath}")
                .Options);

        var catalogCount = await context.Catalogs.CountAsync();
        var nodeCount = await context.CatalogNodes.CountAsync();
        Assert.Equal(1, catalogCount);
        Assert.Equal(2, nodeCount);
    }

    private static async Task SeedLegacyDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE list (
              ID INTEGER,
              Name TEXT,
              ParentID INTEGER,
              Type TEXT,
              Properties TEXT,
              Size INTEGER,
              AID TEXT
            );
            """;
        await create.ExecuteNonQueryAsync();

        await using var insert1 = connection.CreateCommand();
        insert1.CommandText = """
            INSERT INTO list (ID, Name, ParentID, Type, Properties, Size, AID)
            VALUES (1, 'Root', -1, 'scdUnknown', '{}', NULL, 'legacy-a');
            """;
        await insert1.ExecuteNonQueryAsync();

        await using var insert2 = connection.CreateCommand();
        insert2.CommandText = """
            INSERT INTO list (ID, Name, ParentID, Type, Properties, Size, AID)
            VALUES (2, 'File.txt', 1, 'scdFile', '{"ext":"txt"}', 120, 'legacy-a');
            """;
        await insert2.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
