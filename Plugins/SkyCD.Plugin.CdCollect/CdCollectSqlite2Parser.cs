using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Data.Sqlite;
using SkyCD.Native.Sqlite2;
using SkyCD.Plugin.CdCollect.Exceptions;

namespace SkyCD.Plugin.CdCollect;

public static class CdCollectSqlite2Parser
{
    private const string MediaColumns = "id,name,keywords,year,month,day,icon";
    private const string FileColumns = "cd_id,path,name,info,size,mime,type,packed";
    private static readonly Regex Identifier = new("^\\s*(?:\"(?<name>[A-Za-z_][A-Za-z_0-9]*)\"|\\[(?<name>[A-Za-z_][A-Za-z_0-9]*)\\]|(?<name>[A-Za-z_][A-Za-z_0-9]*))\\s+(?<declaration>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static CdCollectReportDocument Parse(Stream source, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default, string? sourcePath = null)
    {
        using var reader = new CdCollectSqlite2Source(source, cancellationToken);
        var tables = CatalogTables(reader);
        var media = Resolve(tables, "cds", MediaColumns);
        var fileTable = tables.ContainsKey("files_v1") ? "files_v1" : "files";
        var files = Resolve(tables, fileTable, FileColumns + (fileTable == "files_v1" ? ",location" : ""));
        var version = 0;
        if (tables.ContainsKey("cdcollect_version"))
        {
            var versionTable = Resolve(tables, "cdcollect_version", "ver");
            var versions = reader.ReadTable(versionTable.Root);
            if (versions.Count != 1 || !int.TryParse(Value(versions[0], versionTable, "ver"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out version))
                throw new CdCollectSqlite2VersionException("invalid version row");
        }
        if (fileTable == "files" ? version != 0 : version != 4)
            throw new CdCollectSqlite2VersionException(version.ToString(CultureInfo.InvariantCulture));
        var path = Path.Combine(Path.GetTempPath(), $"cdcollect_sqlite2_{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ConnectionString))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                Execute("CREATE TABLE cds(id INTEGER PRIMARY KEY NOT NULL,name TEXT,keywords TEXT,year INTEGER,month INTEGER,day INTEGER,icon TEXT)");
                Execute($"CREATE TABLE {fileTable}(cd_id INTEGER,path TEXT,name TEXT,info TEXT,size INTEGER,mime TEXT,type INTEGER,packed INTEGER{(fileTable == "files_v1" ? ",location TEXT" : "")})");
                if (fileTable == "files_v1")
                {
                    Execute("CREATE TABLE cdcollect_version(ver INTEGER)");
                    Execute("INSERT INTO cdcollect_version VALUES(4)");
                }
                Copy(media, "cds", MediaColumns);
                Copy(files, fileTable, FileColumns + (fileTable == "files_v1" ? ",location" : ""));
                transaction.Commit();

                void Copy(Table table, string name, string columnNames)
                {
                    var columns = columnNames.Split(',');
                    foreach (var row in reader.ReadTable(table.Root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (row.Values.Length != table.Columns.Length)
                            throw new CdCollectSqlite2CorruptException($"field count does not match table {name}");
                        var values = columns.Select(column => (object?)Value(row, table, column) ?? DBNull.Value).ToArray();
                        foreach (var column in columns.Where(c => c is "id" or "cd_id" or "year" or "month" or "day" or "size" or "type" or "packed"))
                        {
                            var index = Array.IndexOf(columns, column);
                            if (values[index] is DBNull && column is not ("id" or "cd_id")) values[index] = 0L;
                            if (!long.TryParse(Convert.ToString(values[index], CultureInfo.InvariantCulture), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out var integer))
                                throw new CdCollectSqlite2CorruptException($"non-integer {name}.{column}");
                            values[index] = integer;
                        }
                        Execute($"INSERT INTO {name} VALUES ({string.Join(',', columns.Select((_, i) => "$p" + i))})", values);
                    }
                }

                void Execute(string sql, params object[] values)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, values[i]);
                    command.ExecuteNonQuery();
                }
            }
            using var converted = File.OpenRead(path);
            return CdCollectParser.Parse(converted, progress, cancellationToken, sourcePath);
        }
        catch (SqliteException ex)
        {
            throw new CdCollectSqlite2CorruptException(ex.Message);
        }
        finally { File.Delete(path); }
    }

    internal static bool HasCatalogSchema(Stream source)
    {
        using var reader = new CdCollectSqlite2Source(source, CancellationToken.None);
        var tables = CatalogTables(reader);
        Resolve(tables, "cds", MediaColumns);
        var files = tables.ContainsKey("files_v1") ? "files_v1" : "files";
        Resolve(tables, files, FileColumns + (files == "files_v1" ? ",location" : ""));
        return true;
    }

    private static Dictionary<string, Sqlite2Row> CatalogTables(CdCollectSqlite2Source reader)
    {
        var tables = new Dictionary<string, Sqlite2Row>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in reader.ReadTable(2))
        {
            if (row.Values.Length != 5) throw new CdCollectSqlite2CorruptException("invalid sqlite_master record");
            if (row.Values[0] != "table") continue;
            var name = row.Values[1];
            if (name is null || !tables.TryAdd(name, row)) throw new CdCollectSqlite2CorruptException("duplicate or missing table name");
        }
        if (!tables.ContainsKey("cds") || (!tables.ContainsKey("files") && !tables.ContainsKey("files_v1")))
            throw new CdCollectInvalidFormatException("SQLite 2 database does not contain the CDCollect cds/files catalog schema.");
        return tables;
    }

    private static Table Resolve(Dictionary<string, Sqlite2Row> tables, string name, string required)
    {
        if (!tables.TryGetValue(name, out var row)) throw new CdCollectSqlite2VersionException($"missing table {name}");
        if (!uint.TryParse(row.Values[3], NumberStyles.None, CultureInfo.InvariantCulture, out var root))
            throw new CdCollectSqlite2CorruptException($"invalid root page for {name}");
        var sql = row.Values[4] ?? "";
        var open = sql.IndexOf('(');
        var close = sql.LastIndexOf(')');
        if (!sql.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) || open < 0 || close <= open || sql.Length > 65536)
            throw new CdCollectSqlite2VersionException($"unrecognized {name} definition");
        var columns = new List<string>();
        string? primaryKey = null;
        foreach (var declaration in sql[(open + 1)..close].Split(','))
        {
            var match = Identifier.Match(declaration);
            if (!match.Success) throw new CdCollectSqlite2VersionException($"unrecognized {name} columns");
            var column = match.Groups["name"].Value;
            if (columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                throw new CdCollectSqlite2CorruptException($"duplicate column in {name}");
            columns.Add(column);
            if (Regex.IsMatch(match.Groups["declaration"].Value, "^INTEGER\\s+PRIMARY\\s+KEY(?:\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
                primaryKey = column;
        }
        if (columns.Count > 128 || required.Split(',').Any(c => !columns.Contains(c, StringComparer.OrdinalIgnoreCase)))
            throw new CdCollectSqlite2VersionException($"unrecognized {name} columns");
        return new Table(root, columns.ToArray(), primaryKey);
    }

    private static string? Value(Sqlite2Row row, Table table, string column)
    {
        var index = Array.FindIndex(table.Columns, c => c.Equals(column, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= row.Values.Length) throw new CdCollectSqlite2CorruptException("missing record field");
        return row.Values[index] ?? (column.Equals(table.PrimaryKey, StringComparison.OrdinalIgnoreCase)
            ? row.RowId.ToString(CultureInfo.InvariantCulture) : null);
    }

    private sealed record Table(uint Root, string[] Columns, string? PrimaryKey);
}
