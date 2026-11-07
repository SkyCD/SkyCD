using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SkyCD.Plugin.Legacy.Scd;

internal static class LegacyScdBinaryReader
{
    // VB6 skyFileProperties follows a String * 127 header. Binary-mode Put writes
    // fixed strings as ANSI bytes, Date/Double as eight bytes, and each tree node
    // as three UInt16-length-prefixed ANSI strings (Parent, Key, Text).
    private const int RecordsOffset = 127 + 5392;
    private const int CountOffset = 127 + 128 + 128 + 8;
    private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static LegacyScdCatalog Read(Stream source, CancellationToken cancellationToken)
    {
        if (source.Length < RecordsOffset)
            throw new InvalidLegacyScdBinaryException("truncated header");

        using var reader = new BinaryReader(source, Ansi, leaveOpen: true);
        source.Position = CountOffset;
        var count = reader.ReadDouble();
        if (!double.IsFinite(count) || count < 0 || count > int.MaxValue || count != Math.Truncate(count) ||
            count > (source.Length - RecordsOffset) / 6)
            throw new InvalidLegacyScdBinaryException("invalid node count");

        source.Position = RecordsOffset;
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        for (var i = 0; i < (int)count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = ReadString(reader);
            var key = ReadString(reader);
            var text = ReadString(reader);
            if (key.Length == 0 || text.Length == 0 || !nodes.TryAdd(key, new Node(parent, text)))
                throw new InvalidLegacyScdBinaryException("empty or duplicate node");
        }

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
            if (node.Parent.Length > 0) directories.Add(node.Parent);

        var catalog = new LegacyScdCatalog();
        foreach (var (key, node) in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chain = new Stack<string>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var current = key;
            while (current.Length > 0 && !paths.ContainsKey(current))
            {
                if (!visiting.Add(current) || !nodes.TryGetValue(current, out var ancestor))
                    throw new InvalidLegacyScdBinaryException("missing or cyclic parent");
                chain.Push(current);
                current = ancestor.Parent;
            }

            var path = current.Length == 0 ? "" : paths[current];
            while (chain.TryPop(out var next))
            {
                path = path.Length == 0 ? nodes[next].Text : path + "\\" + nodes[next].Text;
                paths[next] = path;
            }

            var entry = LegacyScdPlugin.ParseEntry(paths[key]);
            catalog.Entries.Add(new LegacyScdEntry
            {
                Path = entry.Path,
                SizeBytes = entry.SizeBytes,
                IsDirectory = node.Parent.Length == 0 || directories.Contains(key) || entry.IsDirectory
            });
        }

        return catalog;
    }

    private static string ReadString(BinaryReader reader)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < 2)
            throw new InvalidLegacyScdBinaryException("truncated string length");
        var length = reader.ReadUInt16();
        if (length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidLegacyScdBinaryException("truncated string");
        var value = Ansi.GetString(reader.ReadBytes(length));
        if (value.Contains('\0')) throw new InvalidLegacyScdBinaryException("null character in node");
        return value;
    }

    private sealed record Node(string Parent, string Text);
}
