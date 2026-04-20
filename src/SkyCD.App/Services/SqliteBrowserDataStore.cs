using Microsoft.Data.Sqlite;
using SkyCD.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services;

public sealed class SqliteBrowserDataStore : IBrowserDataStore, IDisposable
{
    private const string RootKey = "__root__";
    private readonly SqliteConnection connection;

    public SqliteBrowserDataStore()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        InitializeSchema();
        SeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var records = new List<TreeNodeRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, ParentKey, Title, IconGlyph FROM TreeNodes;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new TreeNodeRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        var childrenByParent = records
            .GroupBy(record => record.ParentKey ?? RootKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return BuildTreeNodes(RootKey, childrenByParent);
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        var items = new List<BrowserItem>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Type, Size, IconGlyph FROM BrowserItems WHERE NodeKey = $nodeKey;";
        command.Parameters.AddWithValue("$nodeKey", nodeKey);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new BrowserItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return items;
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodes(
        string parentKey,
        IReadOnlyDictionary<string, List<TreeNodeRecord>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(parentKey, out var children))
        {
            return [];
        }

        return children
            .Select(record => new BrowserTreeNode(
                record.Key,
                record.Title,
                record.IconGlyph,
                BuildTreeNodes(record.Key, childrenByParent),
                isExpanded: record.ParentKey is null))
            .ToArray();
    }

    private void InitializeSchema()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE TreeNodes (
                Key TEXT PRIMARY KEY,
                ParentKey TEXT NULL,
                Title TEXT NOT NULL,
                IconGlyph TEXT NOT NULL
            );

            CREATE TABLE BrowserItems (
                NodeKey TEXT NOT NULL,
                Name TEXT NOT NULL,
                Type TEXT NOT NULL,
                Size TEXT NOT NULL,
                IconGlyph TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private void SeedData()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TreeNodes (Key, ParentKey, Title, IconGlyph) VALUES
            ('library', NULL, 'Library', 'cd'),
            ('movies', 'library', 'Movies', 'folder'),
            ('music', 'library', 'Music', 'folder'),
            ('projects', 'library', 'Projects', 'folder');

            INSERT INTO BrowserItems (NodeKey, Name, Type, Size, IconGlyph) VALUES
            ('library', 'Movies', 'Folder', '128 items', 'folder'),
            ('library', 'Music', 'Folder', '340 items', 'folder'),
            ('library', 'Projects', 'Folder', '56 items', 'folder'),
            ('movies', 'Interstellar.mkv', 'Video', '12.1 GB', 'video'),
            ('movies', 'Arrival.mkv', 'Video', '9.4 GB', 'video'),
            ('music', 'Classical Collection', 'Folder', '42 items', 'folder'),
            ('music', 'Concert-2025.flac', 'Audio', '414 MB', 'audio'),
            ('projects', 'SkyCD v3', 'Folder', '11 items', 'folder'),
            ('projects', 'Plugin Benchmarks', 'Folder', '6 items', 'folder');
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private sealed record TreeNodeRecord(string Key, string? ParentKey, string Title, string IconGlyph);
}
