using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SkyCD.Plugin.Legacy.Scd;

internal static class LegacyScdBinaryWriter
{
    // skyFileProperties from release_XX/modTypes.bas. VB6 Put persists fixed String fields
    // as ANSI bytes and Date/Double values as eight-byte OLE Automation doubles.
    private const int HeaderLength = 127;
    private const int FixedPropertiesLength = 5392;
    private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static void Write(Stream target, LegacyScdCatalog catalog, IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var nodes = CreateNodes(catalog);
        using var writer = new BinaryWriter(target, Ansi, leaveOpen: true);
        WriteFixed(writer, "# SkyCD File\r\n", HeaderLength);
        WriteFixed(writer, "3.0 release", 128);
        WriteFixed(writer, "SkyCD", 128);
        writer.Write(DateTime.Now.ToOADate());
        writer.Write((double)nodes.Count);
        WriteFixed(writer, string.Empty, 128);
        WriteFixed(writer, string.Empty, 128);
        WriteFixed(writer, string.Empty, 256);
        WriteFixed(writer, string.Empty, 512);
        WriteFixed(writer, "Exported by SkyCD. Text not representable in Windows-1252 is replaced with '?'.", 4096);

        for (var index = 0; index < nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[index];
            WriteString(writer, node.Parent);
            WriteString(writer, node.Key);
            WriteString(writer, node.Text);
            progress?.Report((int)((index + 1d) / Math.Max(nodes.Count, 1) * 100d));
        }

        progress?.Report(100);
    }

    private static List<Node> CreateNodes(LegacyScdCatalog catalog)
    {
        var nodes = new List<Node>();
        var keysByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            var segments = entry.Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var parentPath = string.Empty;
            var parentKey = string.Empty;
            for (var index = 0; index < segments.Length; index++)
            {
                var path = parentPath.Length == 0 ? segments[index] : parentPath + "\\" + segments[index];
                if (!keysByPath.TryGetValue(path, out var key))
                {
                    key = $"n{nodes.Count + 1:X7}";
                    keysByPath.Add(path, key);
                    var isLeaf = index == segments.Length - 1;
                    var text = isLeaf && entry.SizeBytes is >= 0
                        ? $"[{LegacyScdSize.Format(entry.SizeBytes.Value)}] {segments[index]}"
                        : segments[index];
                    nodes.Add(new Node(parentKey, key, text));
                }

                parentPath = path;
                parentKey = key;
            }
        }

        return nodes;
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        var bytes = Ansi.GetBytes(value);
        if (bytes.Length > length)
        {
            throw new InvalidLegacyScdBinaryException($"fixed field exceeds its {length}-byte VB6 limit");
        }

        writer.Write(bytes);
        writer.Write(Enumerable.Repeat((byte)' ', length - bytes.Length).ToArray());
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Ansi.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidLegacyScdBinaryException("node field exceeds the VB6 string limit");
        }

        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private sealed record Node(string Parent, string Key, string Text);
}
