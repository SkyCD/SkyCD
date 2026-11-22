using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SkyCD.Native.Sqlite2;
using SkyCD.Plugin.FileFormat.CDCollect.Exceptions;

namespace SkyCD.Plugin.FileFormat.CDCollect.Parsing;

internal sealed class CdCollectSqlite2Source : IDisposable
{
    internal static ReadOnlySpan<byte> Signature => Sqlite2Database.Signature;
    private readonly Sqlite2Database database;
    private readonly Dictionary<uint, string> tables = [];

    internal CdCollectSqlite2Source(Stream source, CancellationToken token)
    {
        try { database = new Sqlite2Database(source, token); }
        catch (Sqlite2UnsupportedFormatException ex) { throw new CdCollectSqlite2VersionException($"SQLite storage format {ex.Format}"); }
        catch (Sqlite2Exception ex) { throw new CdCollectSqlite2CorruptException(ex.Message); }
    }

    internal IReadOnlyList<Sqlite2Row> ReadTable(uint root)
    {
        try
        {
            var rows = database.ReadTable(root == 2 ? "sqlite_master" : tables[root]);
            if (root == 2)
                foreach (var row in rows)
                    if (row.Values.Length == 5 && row.Values[0] == "table" && row.Values[1] is { } name &&
                        uint.TryParse(row.Values[3], NumberStyles.None, CultureInfo.InvariantCulture, out var page))
                        tables[page] = name;
            return rows;
        }
        catch (Sqlite2Exception ex) { throw new CdCollectSqlite2CorruptException(ex.Message); }
    }

    public void Dispose() => database.Dispose();
}
