using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using SkyCD.Plugin.EasyDiskCatalog.Exceptions;
using SQLitePCL;

namespace SkyCD.Plugin.EasyDiskCatalog.Parsing;

/// <summary>
/// Read-only access to an Easy Disk Catalog SQLite database through the isolated
/// SQLite3 Multiple Ciphers codec. Either a legacy RC4-encrypted database opened with
/// the compatibility password, or a plaintext SQLite database, both opened read-only
/// and never modified.
/// </summary>
internal sealed class EasyDiskCatalogDatabase : IDisposable
{
    private static readonly object ProviderGate = new();
    private static bool providerInitialized;

    private readonly sqlite3 db;
    private bool disposed;

    private EasyDiskCatalogDatabase(sqlite3 db)
    {
        this.db = db;
    }

    public static void EnsureProviderInitialized()
    {
        lock (ProviderGate)
        {
            if (providerInitialized)
            {
                return;
            }

            raw.SetProvider(new SQLite3Provider_sqlite3mc());
            providerInitialized = true;
        }
    }

    /// <summary>Opens a plaintext SQLite database read-only.</summary>
    public static EasyDiskCatalogDatabase OpenPlain(string filePath)
    {
        EnsureProviderInitialized();
        var db = OpenUri(EscapeFileUri(filePath), raw.SQLITE_OPEN_READONLY);
        return new EasyDiskCatalogDatabase(db);
    }

    /// <summary>
    /// Opens a legacy RC4-encrypted database read-only with the given UTF-8 password bytes.
    /// When <paramref name="pageSize"/> is null the codec default is used.
    /// </summary>
    public static EasyDiskCatalogDatabase OpenEncrypted(string filePath, byte[] password, int? pageSize)
    {
        EnsureProviderInitialized();
        var uri = new StringBuilder("file:", 64)
            .Append(EscapeFileUri(filePath))
            .Append("?cipher=rc4");
        if (pageSize.HasValue)
        {
            uri.Append("&legacy_page_size=").Append(pageSize.Value);
        }

        var db = OpenUri(uri.ToString(), raw.SQLITE_OPEN_READONLY);
        var handle = new EasyDiskCatalogDatabase(db);
        try
        {
            var rc = raw.sqlite3_key(db, password);
            if (rc != raw.SQLITE_OK)
            {
                throw new EasyDiskCatalogDecryptionException(
                    $"key rejected (result code {rc}).");
            }

            handle.Probe();
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Probe()
    {
        using var statement = Prepare("SELECT count(*) FROM sqlite_master;");
        var rc = raw.sqlite3_step(statement.Handle);
        if (rc != raw.SQLITE_ROW)
        {
            ThrowForResult(rc, "catalog header cannot be decoded");
        }
    }

    public HashSet<string> ReadTableNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var statement = Prepare("SELECT name FROM sqlite_master WHERE type = 'table';");
        while (raw.sqlite3_step(statement.Handle) == raw.SQLITE_ROW)
        {
            var name = raw.sqlite3_column_text(statement.Handle, 0).utf8_to_string();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public List<string> ReadColumnNames(string table)
    {
        var columns = new List<string>();
        using var statement = Prepare($"PRAGMA table_info({QuoteIdentifier(table)});");
        while (raw.sqlite3_step(statement.Handle) == raw.SQLITE_ROW)
        {
            var name = raw.sqlite3_column_text(statement.Handle, 1).utf8_to_string();
            if (!string.IsNullOrWhiteSpace(name))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    public List<Dictionary<string, object?>> QueryAll(
        string table,
        IReadOnlyList<string> columns,
        string? whereClause,
        long[] integerParameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(integerParameters);

        var sql = new StringBuilder("SELECT ", 128);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append(QuoteIdentifier(columns[i]));
        }

        sql.Append(" FROM ").Append(QuoteIdentifier(table));
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql.Append(' ').Append(whereClause);
        }

        sql.Append(';');
        var rows = new List<Dictionary<string, object?>>();
        using var statement = Prepare(sql.ToString());
        BindIntegers(statement.Handle, integerParameters);
        var processed = 0;
        int rc;
        while ((rc = raw.sqlite3_step(statement.Handle)) == raw.SQLITE_ROW)
        {
            if ((++processed & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            rows.Add(ReadRow(statement.Handle, columns));
        }

        if (rc != raw.SQLITE_DONE)
        {
            ThrowForResult(rc, $"query against '{table}' failed");
        }

        return rows;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        raw.sqlite3_close_v2(db);
    }

    private static sqlite3 OpenUri(string filename, int flags)
    {
        var rc = raw.sqlite3_open_v2(
            filename,
            out var db,
            flags | raw.SQLITE_OPEN_URI,
            null);
        if (rc != raw.SQLITE_OK || db is null)
        {
            if (db is not null)
            {
                raw.sqlite3_close_v2(db);
            }

            throw new EasyDiskCatalogCorruptException(
                $"cannot open database file (result code {rc}).");
        }

        return db;
    }

    private PreparedStatement Prepare(string sql)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var rc = raw.sqlite3_prepare_v2(db, sql, out var handle);
        if (rc != raw.SQLITE_OK || handle is null)
        {
            ThrowForResult(rc, "statement preparation failed");
            throw new EasyDiskCatalogCorruptException("statement preparation failed.");
        }

        return new PreparedStatement(handle);
    }

    private static void BindIntegers(sqlite3_stmt handle, long[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var rc = raw.sqlite3_bind_int64(handle, i + 1, values[i]);
            if (rc != raw.SQLITE_OK)
            {
                throw new EasyDiskCatalogCorruptException(
                    $"cannot bind query parameter {i + 1} (result code {rc}).");
            }
        }
    }

    private static Dictionary<string, object?> ReadRow(sqlite3_stmt handle, IReadOnlyList<string> columns)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            row[columns[i]] = raw.sqlite3_column_type(handle, i) switch
            {
                raw.SQLITE_NULL => null,
                raw.SQLITE_INTEGER => raw.sqlite3_column_int64(handle, i),
                raw.SQLITE_FLOAT => raw.sqlite3_column_double(handle, i),
                raw.SQLITE_TEXT => raw.sqlite3_column_text(handle, i).utf8_to_string(),
                _ => raw.sqlite3_column_blob(handle, i).ToArray(),
            };
        }

        return row;
    }

    private void ThrowForResult(int rc, string action)
    {
        var detail = raw.sqlite3_errmsg(db).utf8_to_string();
        throw rc == raw.SQLITE_NOTADB
            ? new EasyDiskCatalogDecryptionException($"{action}: {detail}")
            : new EasyDiskCatalogCorruptException($"{action} (result code {rc}): {detail}");
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static string EscapeFileUri(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return filePath.Replace("?", "%3f", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal);
    }

    private sealed class PreparedStatement(sqlite3_stmt handle) : IDisposable
    {
        public sqlite3_stmt Handle { get; } = handle;

        public void Dispose()
        {
            raw.sqlite3_finalize(Handle);
        }
    }
}
