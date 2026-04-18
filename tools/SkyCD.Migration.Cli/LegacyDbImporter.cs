using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SkyCD.Domain.Catalogs;
using SkyCD.Infrastructure.Persistence;

namespace SkyCD.Migration.Cli;

public sealed class LegacyDbImporter
{
    public async Task<LegacyImportResult> ImportAsync(string legacyPath, string targetPath, bool dryRun, CancellationToken cancellationToken = default)
    {
        var targetConnection = $"Data Source={targetPath}";
        await using var targetContext = new SkyCdDbContext(
            new DbContextOptionsBuilder<SkyCdDbContext>()
                .UseSqlite(targetConnection)
                .Options);

        await targetContext.Database.MigrateAsync(cancellationToken);

        await using var legacyConnection = new SqliteConnection($"Data Source={legacyPath};Mode=ReadOnly");
        await legacyConnection.OpenAsync(cancellationToken);

        var rows = await ReadLegacyRowsAsync(legacyConnection, cancellationToken);
        if (rows.Count == 0)
        {
            return new LegacyImportResult(0, 0, []);
        }

        var groupedByAid = rows.GroupBy(row => row.Aid).ToList();
        var importedCatalogs = 0;
        var importedNodes = 0;
        var errors = new List<string>();

        foreach (var aidGroup in groupedByAid)
        {
            var catalog = new Catalog
            {
                Name = $"Imported Legacy Catalog ({aidGroup.Key})",
                SchemaVersion = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            foreach (var row in aidGroup)
            {
                var kind = row.Type.Equals("scdFile", StringComparison.OrdinalIgnoreCase)
                    ? CatalogNodeKind.File
                    : CatalogNodeKind.Folder;

                catalog.Nodes.Add(new CatalogNode
                {
                    Id = row.Id,
                    CatalogId = catalog.Id,
                    ParentId = row.ParentId < 0 ? null : row.ParentId,
                    Kind = kind,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? $"Unnamed-{row.Id}" : row.Name,
                    SizeBytes = kind == CatalogNodeKind.File ? row.Size : null,
                    MetadataJson = row.Properties
                });
                importedNodes++;
            }

            var validationErrors = CatalogValidator.Validate(catalog);
            if (validationErrors.Count > 0)
            {
                errors.AddRange(validationErrors.Select(error => $"Catalog '{catalog.Name}': {error}"));
                continue;
            }

            if (!dryRun)
            {
                await targetContext.Catalogs.AddAsync(catalog, cancellationToken);
                await targetContext.SaveChangesAsync(cancellationToken);
            }

            importedCatalogs++;
        }

        return new LegacyImportResult(importedCatalogs, importedNodes, errors);
    }

    private static async Task<IReadOnlyList<LegacyRow>> ReadLegacyRowsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyRow>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ID, Name, ParentID, Type, Properties, Size, AID FROM list ORDER BY AID, ID";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LegacyRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? -1 : reader.GetInt64(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                reader.IsDBNull(6) ? "default" : reader.GetValue(6).ToString() ?? "default"));
        }

        return rows;
    }
}

public sealed record LegacyImportResult(int ImportedCatalogs, int ImportedNodes, IReadOnlyCollection<string> Errors);

public sealed record LegacyRow(
    long Id,
    string Name,
    long ParentId,
    string Type,
    string? Properties,
    long? Size,
    string Aid);
