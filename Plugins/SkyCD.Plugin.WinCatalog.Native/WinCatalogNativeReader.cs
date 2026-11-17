using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.WinCatalog.Native.Exceptions;

namespace SkyCD.Plugin.WinCatalog.Native;

/// <summary>
/// Reads a native WinCatalog catalog database from a consistent read-only
/// snapshot. The source stream is materialized to a temp file, opened
/// read-only with query-only enforcement, and never modified.
/// </summary>
public static class WinCatalogNativeReader
{
    public const int MaxItemCount = 1_000_000;
    public const int MaxIdentifierLength = 1024;
    public const long MaxBlobBytes = 8L * 1024 * 1024;
    private const int CancelCheckEveryRows = 500;

    public sealed record TableSnapshot(string Name, List<string> Columns);

    public sealed record Snapshot(
        List<TableSnapshot> Tables,
        List<GenericRecord> Records,
        List<string> Diagnostics,
        string? SchemaVersion);

    public sealed record GenericRecord(
        string Table,
        string Name,
        string? FullPath,
        string? ParentPath,
        string Kind,
        long Size,
        string? Created,
        string? Modified,
        string? Comment,
        string? Tags,
        IReadOnlyDictionary<string, string> Extra);

    public static async Task<Snapshot> ReadAsync(
        Stream source,
        string? sourcePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshotPath = Path.Combine(
            Path.GetTempPath(), $"skycd-wincatalog-{Guid.NewGuid():N}.db");
        try
        {
            using (var fileStream = File.Create(snapshotPath))
            {
                await source.CopyToAsync(fileStream, cancellationToken);
            }

            progress?.Report(25);
            return await ReadSnapshotAsync(snapshotPath, progress, cancellationToken);
        }
        finally
        {
            DeleteQuietly(snapshotPath);
            DeleteQuietly(snapshotPath + "-wal");
            DeleteQuietly(snapshotPath + "-shm");
            DeleteQuietly(snapshotPath + "-journal");
        }
    }

    private static async Task<Snapshot> ReadSnapshotAsync(
        string snapshotPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString;

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA query_only = ON";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var tables = await ReadTablesAsync(connection, cancellationToken);
            if (tables.Count == 0)
            {
                throw new WinCatalogNativeCorruptException();
            }

            var candidateTables = tables
                .Where(t => LooksLikeCatalogTable(t.Name))
                .ToList();
            if (candidateTables.Count == 0)
            {
                throw new WinCatalogNativeUnsupportedSchemaException(
                    "no WinCatalog catalog tables were found.");
            }

            var schemaVersion = await TryReadVersionAsync(connection, tables, cancellationToken);
            if (schemaVersion is not null)
            {
                diagnostics.Add($"Detected schema marker '{schemaVersion}'.");
            }
            else
            {
                diagnostics.Add("No WinCatalog version marker was found; proceeding with column-tolerant reads.");
            }

            var records = new List<GenericRecord>();
            foreach (var table in candidateTables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReadTableRecordsAsync(connection, table, records, diagnostics, cancellationToken);
                progress?.Report(60);
                if (records.Count > MaxItemCount)
                {
                    throw new WinCatalogNativeCorruptException();
                }
            }

            if (records.Count == 0)
            {
                throw new WinCatalogNativeEmptyCatalogException();
            }

            diagnostics.Add("Thumbnails, icons, search history, and catalog settings are not imported.");
            return new Snapshot(candidateTables, records, diagnostics, schemaVersion);
        }
        catch (SqliteException exception)
        {
            throw new WinCatalogNativeCorruptException(exception.Message);
        }
    }

    private static bool LooksLikeCatalogTable(string name)
    {
        return name.Contains("catalog", StringComparison.OrdinalIgnoreCase)
            || name.Contains("disk", StringComparison.OrdinalIgnoreCase)
            || name.Contains("volume", StringComparison.OrdinalIgnoreCase)
            || name.Contains("folder", StringComparison.OrdinalIgnoreCase)
            || name.Contains("file", StringComparison.OrdinalIgnoreCase)
            || name.Contains("entry", StringComparison.OrdinalIgnoreCase)
            || name.Contains("entries", StringComparison.OrdinalIgnoreCase)
            || name.Contains("media", StringComparison.OrdinalIgnoreCase)
            || name.Contains("item", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<TableSnapshot>> ReadTablesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<TableSnapshot>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tables.Add(new TableSnapshot(name, await ReadColumnNamesAsync(connection, name, cancellationToken)));
        }

        return tables;
    }

    private static async Task<List<string>> ReadColumnNamesAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task<string?> TryReadVersionAsync(
        SqliteConnection connection, List<TableSnapshot> tables, CancellationToken cancellationToken)
    {
        foreach (var table in tables)
        {
            var versionColumn = table.Columns.FirstOrDefault(c =>
                c.Contains("version", StringComparison.OrdinalIgnoreCase) ||
                c.Contains("app_version", StringComparison.OrdinalIgnoreCase));
            if (versionColumn is null)
            {
                continue;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {QuoteIdentifier(versionColumn)} FROM {QuoteIdentifier(table.Name)} LIMIT 1";
                var value = await command.ExecuteScalarAsync(cancellationToken);
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 64)
                {
                    return $"{table.Name}.{versionColumn}={text}";
                }
            }
            catch (SqliteException)
            {
                continue;
            }
        }

        return null;
    }

    private static async Task ReadTableRecordsAsync(
        SqliteConnection connection,
        TableSnapshot table,
        List<GenericRecord> records,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var columns = table.Columns;
        var nameCol = PickColumn(columns, "name", "filename", "title", "caption");
        if (nameCol is null)
        {
            diagnostics.Add($"Table '{table.Name}' has no name column; skipped.");
            return;
        }

        var pathCol = PickColumn(columns, "fullpath", "full_path", "path", "catalogpath", "catalog_path", "filepath", "file_path");
        var parentCol = PickColumn(columns, "parent", "parentpath", "parent_path", "parent_id", "folder");
        if (pathCol is null && parentCol is null)
        {
            diagnostics.Add($"Table '{table.Name}' has no path column; skipped.");
            return;
        }
        var kindCol = PickColumn(columns, "kind", "type", "isdir", "is_dir", "is_folder", "objecttype", "object_type", "entrytype", "entry_type");
        var sizeCol = PickColumn(columns, "size", "filesize", "file_size", "length", "bytes");
        var createdCol = PickColumn(columns, "created", "datecreated", "date_created", "ctime");
        var modifiedCol = PickColumn(columns, "modified", "datemodified", "date_modified", "mtime", "updated");
        var commentCol = PickColumn(columns, "comment", "comments", "description", "notes");
        var tagsCol = PickColumn(columns, "tags", "keywords", "labels");

        var select = new List<string> { QuoteIdentifier(nameCol) };
        foreach (var col in new[] { pathCol, parentCol, kindCol, sizeCol, createdCol, modifiedCol, commentCol, tagsCol })
        {
            if (col is not null && !select.Contains(QuoteIdentifier(col), StringComparer.Ordinal))
            {
                select.Add(QuoteIdentifier(col));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", select)} FROM {QuoteIdentifier(table.Name)} LIMIT {MaxItemCount + 1}";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++rows % CancelCheckEveryRows == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (records.Count >= MaxItemCount)
            {
                throw new WinCatalogNativeCorruptException();
            }

            string? Get(string? column)
            {
                if (column is null)
                {
                    return null;
                }

                var ordinal = select.FindIndex(s => s.Equals(QuoteIdentifier(column), StringComparison.Ordinal));
                if (ordinal < 0 || reader.IsDBNull(ordinal))
                {
                    return null;
                }

                var raw = reader.GetValue(ordinal);
                if (raw is byte[] bytes)
                {
                    if (bytes.Length > MaxBlobBytes)
                    {
                        return null;
                    }

                    try
                    {
                        return System.Text.Encoding.UTF8.GetString(bytes).Trim();
                    }
                    catch
                    {
                        return null;
                    }
                }

                var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
                if (text is not null && text.Length > MaxIdentifierLength * 4)
                {
                    text = text[..(MaxIdentifierLength * 4)];
                }

                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            var name = Get(nameCol)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxIdentifierLength)
            {
                continue;
            }

            var kindText = Get(kindCol) ?? string.Empty;
            var kind = kindText.Contains("folder", StringComparison.OrdinalIgnoreCase)
                || kindText.Contains("dir", StringComparison.OrdinalIgnoreCase)
                || kindText.Equals("1") && (nameCol.Contains("folder", StringComparison.OrdinalIgnoreCase) || table.Name.Contains("folder", StringComparison.OrdinalIgnoreCase))
                ? "folder"
                : table.Name.Contains("folder", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(sizeCol)
                    ? "folder"
                    : "file";

            long size = 0;
            var sizeText = Get(sizeCol);
            if (!string.IsNullOrWhiteSpace(sizeText))
            {
                var digits = new string(sizeText.Where(char.IsDigit).ToArray());
                _ = long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            }

            records.Add(new GenericRecord(
                table.Name, name, Get(pathCol), Get(parentCol), kind, size,
                Get(createdCol), Get(modifiedCol), Get(commentCol), Get(tagsCol),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static string? PickColumn(List<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var exact = columns.FirstOrDefault(c => c.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var candidate in candidates)
        {
            var partial = columns.FirstOrDefault(c => c.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (partial is not null)
            {
                return partial;
            }
        }

        return null;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
