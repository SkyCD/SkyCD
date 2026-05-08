using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Migration.Cli;

public sealed class LegacyDbImporter
{
    public async Task<LegacyImportResult> ImportAsync(string legacyPath, string targetPath, bool dryRun, CancellationToken cancellationToken = default)
    {
        _ = targetPath;

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
            var now = DateTimeOffset.UtcNow;
            var catalogId = Guid.NewGuid().ToString("N");
            var catalogName = $"Imported Legacy Catalog ({aidGroup.Key})";
            var rootId = $"catalog-{catalogId}";
            var root = new CatalogDocument
            {
                Id = rootId,
                Name = catalogName,
                ParentId = null,
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 0
            };

            var documents = new List<CatalogDocument> { root };

            foreach (var row in aidGroup)
            {
                var type = row.Type.Equals("scdFile", StringComparison.OrdinalIgnoreCase)
                    ? CatalogDocumentType.File
                    : CatalogDocumentType.Folder;
                var parentId = row.ParentId < 0 ? rootId : $"{catalogId}-{row.ParentId}";
                var id = $"{catalogId}-{row.Id}";

                documents.Add(new CatalogDocument
                {
                    Id = id,
                    ParentId = parentId,
                    Type = type,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? $"Unnamed-{row.Id}" : row.Name,
                    Size = type == CatalogDocumentType.File ? (row.Size ?? 0) : 0,
                    ChildrenCount = 0
                });
                importedNodes++;
            }

            var validationErrors = ValidateCatalogDocuments(catalogName, documents);
            if (validationErrors.Count > 0)
            {
                errors.AddRange(validationErrors);
                continue;
            }

            if (!dryRun)
            {
                errors.Add($"Catalog '{catalogName}': Persisting imports is not supported after infrastructure removal. Use dry-run mode.");
                continue;
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

    private static IReadOnlyList<string> ValidateCatalogDocuments(string catalogName, IReadOnlyList<CatalogDocument> documents)
    {
        var errors = new List<string>();

        if (documents.Count == 0)
        {
            errors.Add($"Catalog '{catalogName}': No documents to import.");
            return errors;
        }

        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Name))
            {
                errors.Add($"Catalog '{catalogName}': Document '{document.Id}' name is required.");
            }

            if (document.Type == CatalogDocumentType.Folder && document.Size != 0)
            {
                errors.Add($"Catalog '{catalogName}': Folder '{document.Id}' size must be zero.");
            }

            if (document.Size < 0)
            {
                errors.Add($"Catalog '{catalogName}': Document '{document.Id}' size cannot be negative.");
            }
        }

        return errors;
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
