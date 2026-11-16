using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.CdCollect.Exceptions;

namespace SkyCD.Plugin.CdCollect;

internal static class CdCollectWriter
{
    private const string Schema = """
        CREATE TABLE cds (id INTEGER PRIMARY KEY NOT NULL, name TEXT, keywords TEXT, year INTEGER, month INTEGER, day INTEGER, icon TEXT);
        CREATE TABLE files_v1 (cd_id INTEGER, path TEXT, name TEXT, info TEXT, size INTEGER, mime TEXT, location TEXT, type INTEGER, packed INTEGER);
        CREATE TABLE cdcollect_version (ver INTEGER);
        INSERT INTO cdcollect_version VALUES (4);
        CREATE TABLE infoplugins (mime TEXT UNIQUE, program TEXT, arguments TEXT, regex_input TEXT, regex_pattern TEXT, regex_case INTEGER);
        CREATE TABLE compplugins (mime TEXT UNIQUE, program TEXT, arguments TEXT);
        """;

    public static void Write(FileFormatWriteRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var payload = request.Payload is CdCollectReportDocument report ? report.Rows : request.Payload;
        var json = JsonSerializer.SerializeToElement(payload, options);
        if (json.ValueKind != JsonValueKind.Array)
            throw new CdCollectInvalidFormatException("Export requires a catalog row array.");
        var rows = json.EnumerateArray().ToList();
        var nodes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = Text(row, "Id", "NodeId");
            if (id.Length == 0 || !nodes.TryAdd(id, row))
                throw new CdCollectInvalidFormatException("Export requires nonempty, unique node IDs.");
        }
        foreach (var row in rows)
        {
            var parent = Text(row, "ParentId");
            if (parent.Length != 0 && !nodes.ContainsKey(parent))
                throw new CdCollectInvalidFormatException($"Missing parent '{parent}'.");
        }
        var roots = rows.Where(r => Text(r, "ParentId").Length == 0).ToList();
        if (roots.Count == 0) throw new CdCollectEmptyCatalogException();
        var children = rows.Where(r => Text(r, "ParentId").Length != 0)
            .ToLookup(r => Text(r, "ParentId"), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"cdcollect_export_{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ConnectionString))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                Execute(Schema);
                var mediaId = 0;
                foreach (var root in roots)
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsType(root, "Media"))
                        throw new CdCollectInvalidFormatException("Export requires Media roots; place loose files and folders inside media first.");
                    mediaId++;
                    visited.Add(Text(root, "Id", "NodeId"));
                    var props = Get(root, "Properties");
                    DateTime.TryParse(Text(props, "Date", "DateAdded", "DateCreated"), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date);
                    Execute("INSERT INTO cds VALUES ($a,$b,$c,$d,$e,$f,$g)", mediaId, Text(root, "Name"),
                        Text(props, "Keywords", "Comments"), date == default ? 0 : date.Year,
                        date == default ? 0 : date.Month, date == default ? 0 : date.Day, Text(props, "Icon"));
                    var queue = new Queue<(JsonElement Node, string Path, string Location)>();
                    EnqueueChildren(root, "", "/");
                    while (queue.TryDequeue(out var item))
                    {
                        token.ThrowIfCancellationRequested();
                        var node = item.Node;
                        if (!visited.Add(Text(node, "Id", "NodeId")))
                            throw new CdCollectInvalidFormatException("Catalog hierarchy contains a cycle.");
                        if (IsType(node, "Media"))
                            throw new CdCollectInvalidFormatException("Nested media cannot be represented in CDCollect.");
                        var directory = IsType(node, "Folder") || IsType(node, "NetworkFolder") || IsType(node, "NetworkResource");
                        var properties = Get(node, "Properties");
                        if (!long.TryParse(Text(node, "Size", "SizeBytes"), CultureInfo.InvariantCulture, out var size)) size = 0;
                        // CDCollect 0.6.x reads sizes as signed Int32. Fail rather than silently truncate.
                        if (size < 0 || size > int.MaxValue)
                            throw new CdCollectInvalidFormatException($"Entry '{Text(node, "Name")}' exceeds CDCollect's signed 32-bit size limit.");
                        var packed = Text(properties, "IsPacked", "Packed");
                        Execute("INSERT INTO files_v1 VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i)", mediaId, item.Path,
                            Text(node, "Name"), Text(properties, "Info", "Comments", "Description"), size,
                            Text(properties, "MimeType", "Mime"), Text(properties, "Location", "Path") is { Length: > 0 } location ? location : item.Location,
                            directory ? 0 : 1, packed.Equals("true", StringComparison.OrdinalIgnoreCase) || packed == "1" ? 1 : 0);
                        if (!directory && children[Text(node, "Id", "NodeId")].Any() && packed != "true" && packed != "1")
                            throw new CdCollectInvalidFormatException("Only folders or packed archives may contain children.");
                        EnqueueChildren(node, item.Path, item.Location.TrimEnd('/') + "/" + Text(node, "Name"));
                    }

                    void EnqueueChildren(JsonElement parent, string prefix, string location)
                    {
                        var index = 0;
                        foreach (var child in children[Text(parent, "Id", "NodeId")])
                        {
                            var key = index++.ToString(CultureInfo.InvariantCulture);
                            queue.Enqueue((child, prefix.Length == 0 ? key : prefix + ":" + key, location));
                        }
                    }
                }
                if (visited.Count != rows.Count)
                    throw new CdCollectInvalidFormatException("Catalog contains an unreachable hierarchy or cycle.");
                transaction.Commit();

                void Execute(string sql, params object[] values)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$" + (char)('a' + i), values[i]);
                    command.ExecuteNonQuery();
                }
            }
            token.ThrowIfCancellationRequested();
            using var source = File.OpenRead(path);
            source.CopyTo(request.Target);
            request.Progress?.Report(100);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static JsonElement Get(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string Text(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Get(node, name);
            if (value.ValueKind != JsonValueKind.Undefined && value.ValueKind != JsonValueKind.Null)
                return value.ToString();
        }
        return string.Empty;
    }

    private static bool IsType(JsonElement node, string type) => Text(node, "Type", "Kind").Equals(type, StringComparison.OrdinalIgnoreCase);
}
