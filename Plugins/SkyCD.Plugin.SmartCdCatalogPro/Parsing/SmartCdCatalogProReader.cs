using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetDatabaseWriter;
using JetDatabaseWriter.Exceptions;
using SkyCD.Plugin.SmartCdCatalogPro.Exceptions;
using SkyCD.Plugin.SmartCdCatalogPro.Models;

namespace SkyCD.Plugin.SmartCdCatalogPro.Parsing;

/// <summary>
/// Reads the named tables of a Smart CD Catalog PRO Jet 4 database. Only the tables and
/// columns listed here are touched; macros, queries and cached search tables are ignored.
/// </summary>
internal static class SmartCdCatalogProReader
{
    // Fixed format constant embedded in the legacy reader (";pwd=chpok"). It is a
    // database password, not a user credential, and is never logged.
    internal const string FixedDatabasePassword = "chpok";

    private const int MaxCoreRows = 5_000_000;
    private const int MaxClassificationRows = 500_000;
    private const int MaxLoanRows = 5_000_000;

    private static readonly (string Table, string[] Columns)[] RequiredSchema =
    [
        ("CDs", ["IDCD", "Drive_Folder"]),
        ("Paths", ["IDPATH", "IDCD", "Path", "IsArchive"]),
        ("Elements", ["ID", "IDPATH", "ElementName", "IsFolder"])
    ];

    /// <summary>
    /// Opens the database with the fixed password and verifies the expected core schema.
    /// Used by content detection so arbitrary Jet databases are never claimed.
    /// </summary>
    public static async Task<bool> HasExpectedSchemaAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await OpenReaderAsync(path, cancellationToken).ConfigureAwait(false);
            await VerifySchemaAsync(reader, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<SmartCdCatalogProSnapshot> ReadAsync(
        string path,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var reader = await OpenReaderAsync(path, cancellationToken).ConfigureAwait(false);
        var schema = await VerifySchemaAsync(reader, cancellationToken).ConfigureAwait(false);
        progress?.Report(15);

        var cds = await ReadTableAsync(reader, schema.Tables["CDs"], schema.Columns["CDs"], MaxCoreRows, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(30);
        var paths = await ReadTableAsync(reader, schema.Tables["Paths"], schema.Columns["Paths"], MaxCoreRows, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(45);
        var elements = await ReadTableAsync(reader, schema.Tables["Elements"], schema.Columns["Elements"], MaxCoreRows, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(60);

        var categories = await ReadOptionalTableAsync(reader, schema, "Categories", MaxClassificationRows, cancellationToken)
            .ConfigureAwait(false);
        var boxes = await ReadOptionalTableAsync(reader, schema, "Boxes", MaxClassificationRows, cancellationToken)
            .ConfigureAwait(false);
        var borrows = await ReadOptionalTableAsync(reader, schema, "Borrows", MaxClassificationRows, cancellationToken)
            .ConfigureAwait(false);
        var loans = await ReadOptionalTableAsync(reader, schema, "LoanHistory", MaxLoanRows, cancellationToken)
            .ConfigureAwait(false);
        var accessModeRows = await ReadOptionalTableAsync(reader, schema, "AccessMode", 2, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(70);

        return new SmartCdCatalogProSnapshot
        {
            Cds = cds,
            Paths = paths,
            Elements = elements,
            Categories = categories,
            Boxes = boxes,
            Borrows = borrows,
            LoanHistory = loans,
            AccessMode = ReadAccessMode(accessModeRows)
        };
    }

    private static async Task<AccessReader> OpenReaderAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await AccessReader.OpenAsync(
                    path,
                    new AccessReaderOptions(FixedDatabasePassword)
                    {
                        FileAccess = FileAccess.Read,
                        FileShare = FileShare.Read,
                        UseLockFile = false,
                        ValidateOnOpen = true,
                        PageCacheSize = 256
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JetAccessDeniedException)
        {
            throw new SmartCdCatalogProInvalidFormatException(
                "the fixed Jet database password was rejected");
        }
        catch (JetNotSupportedException)
        {
            throw new SmartCdCatalogProInvalidFormatException(
                "the database uses an unsupported Jet or encryption format");
        }
        catch (JetCorruptDataException)
        {
            throw new SmartCdCatalogProCorruptCatalogException("the Jet header or catalog pages are damaged");
        }
        catch (IOException)
        {
            throw new SmartCdCatalogProCorruptCatalogException("the database could not be read");
        }
    }

    private static async Task<CatalogSchema> VerifySchemaAsync(AccessReader reader, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tableNames;
        try
        {
            tableNames = await reader.ListTablesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw WrapReadFailure(exception);
        }

        var tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in tableNames)
        {
            tables[tableName] = tableName;
        }

        var columnsByTable = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, requiredColumns) in RequiredSchema)
        {
            if (!tables.TryGetValue(table, out var actualTable))
            {
                throw new SmartCdCatalogProUnsupportedSchemaException(
                    $"the required '{table}' table is missing");
            }

            IReadOnlyList<string> columns;
            try
            {
                columns = (await reader.GetColumnMetadataAsync(actualTable, cancellationToken)
                        .ConfigureAwait(false))
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Name)
                    .ToArray();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw WrapReadFailure(exception);
            }

            var columnSet = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            foreach (var requiredColumn in requiredColumns)
            {
                if (!columnSet.Contains(requiredColumn))
                {
                    throw new SmartCdCatalogProUnsupportedSchemaException(
                        $"the required '{actualTable}.{requiredColumn}' column is missing");
                }
            }

            columnsByTable[actualTable] = columns;
        }

        return new CatalogSchema(tables, columnsByTable);
    }

    private static SmartCdAccessMode? ReadAccessMode(IReadOnlyList<SmartCdRow> rows)
    {
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return new SmartCdAccessMode(
            DefaultIsFull: row.GetBoolean("DefaultIsFull") ?? true,
            HasFullPassword: !string.IsNullOrEmpty(row.GetString("FullPW")),
            HasReadOnlyPassword: !string.IsNullOrEmpty(row.GetString("ReadOnlyPW")));
    }

    private static async Task<IReadOnlyList<SmartCdRow>> ReadOptionalTableAsync(
        AccessReader reader,
        CatalogSchema schema,
        string table,
        int maxRows,
        CancellationToken cancellationToken)
    {
        if (!schema.Tables.TryGetValue(table, out var actualTable))
        {
            return [];
        }

        IReadOnlyList<string> columns;
        try
        {
            columns = (await reader.GetColumnMetadataAsync(actualTable, cancellationToken).ConfigureAwait(false))
                .OrderBy(column => column.Ordinal)
                .Select(column => column.Name)
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw WrapReadFailure(exception);
        }

        return await ReadTableAsync(reader, actualTable, columns, maxRows, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SmartCdRow>> ReadTableAsync(
        AccessReader reader,
        string table,
        IReadOnlyList<string> columns,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var rows = new List<SmartCdRow>();
        try
        {
            await foreach (var values in reader.Rows(table, null, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rows.Count >= maxRows)
                {
                    throw new SmartCdCatalogProCorruptCatalogException(
                        $"the '{table}' table exceeds the supported {maxRows} row limit");
                }

                rows.Add(new SmartCdRow(table, columns, values));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw WrapReadFailure(exception);
        }

        return rows;
    }

    private static Exception WrapReadFailure(Exception exception) => exception switch
    {
        SmartCdCatalogProCorruptCatalogException or
        SmartCdCatalogProUnsupportedSchemaException or
        SmartCdCatalogProInvalidFormatException => exception,
        JetCorruptDataException => new SmartCdCatalogProCorruptCatalogException(
            $"damaged catalog pages ({exception.Message})"),
        JetAccessDeniedException => new SmartCdCatalogProInvalidFormatException(
            "the fixed Jet database password was rejected"),
        JetObjectNotFoundException => new SmartCdCatalogProUnsupportedSchemaException(
            $"a referenced table is missing ({exception.Message})"),
        IOException => new SmartCdCatalogProCorruptCatalogException(
            $"the database could not be read ({exception.Message})"),
        _ => new SmartCdCatalogProCorruptCatalogException(
            $"the database could not be read ({exception.Message})")
    };

    private sealed record CatalogSchema(
        IReadOnlyDictionary<string, string> Tables,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Columns);
}
