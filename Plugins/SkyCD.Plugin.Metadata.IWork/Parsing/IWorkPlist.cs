using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SkyCD.Plugin.Metadata.IWork.Parsing;

internal abstract record PlistNode;

internal sealed record PlistString(string Value) : PlistNode;

internal sealed record PlistBoolean(bool Value) : PlistNode;

internal sealed record PlistInteger(long Value) : PlistNode;

internal sealed record PlistReal(double Value) : PlistNode;

internal sealed record PlistArray(IReadOnlyList<PlistNode> Items) : PlistNode;

internal sealed record PlistDictionary(IReadOnlyDictionary<string, PlistNode> Items) : PlistNode;

/// <summary>
/// Reads the subset of the XML property-list grammar the iWork metadata members use.
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant: a member that is absent, binary, malformed or deeper than the bound yields
/// <see langword="null"/> so the corresponding fields are omitted instead of failing the whole extraction.
/// External entities and the Apple DTD are never resolved, so a crafted property list cannot reach the disk or
/// the network.
/// </remarks>
internal static class IWorkPlist
{
    private const int MaxDepth = 32;

    public static PlistDictionary? TryReadDictionary(byte[] data) => TryReadRoot(data) as PlistDictionary;

    public static PlistNode? TryReadRoot(byte[] data)
    {
        if (!LooksLikeXml(data))
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = true
            };

            using var stream = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var value = string.Equals(root.Name.LocalName, "plist", StringComparison.Ordinal)
                ? root.Elements().FirstOrDefault()
                : root;

            return value is null ? null : ParseElement(value, 0);
        }
        catch (XmlException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("bplist00"u8))
        {
            return false;
        }

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            data = data[3..];
        }

        foreach (var value in data)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return value == (byte)'<';
        }

        return false;
    }

    private static PlistNode? ParseElement(XElement element, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (element.Name.LocalName)
        {
            case "dict":
                return ParseDictionary(element, depth);
            case "array":
                return ParseArray(element, depth);
            case "string":
                return new PlistString(element.Value);
            case "integer":
                return long.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var integer)
                    ? new PlistInteger(integer)
                    : null;
            case "real":
                return double.TryParse(element.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var real)
                    ? new PlistReal(real)
                    : null;
            case "true":
                return new PlistBoolean(true);
            case "false":
                return new PlistBoolean(false);
            default:
                // Dates, data blobs and unknown elements carry nothing this plugin publishes.
                return null;
        }
    }

    private static PlistDictionary ParseDictionary(XElement element, int depth)
    {
        var items = new Dictionary<string, PlistNode>(StringComparer.Ordinal);
        string? key = null;
        foreach (var child in element.Elements())
        {
            if (string.Equals(child.Name.LocalName, "key", StringComparison.Ordinal))
            {
                key = child.Value;
                continue;
            }

            if (key is null)
            {
                continue;
            }

            var value = ParseElement(child, depth + 1);
            if (value is not null)
            {
                items[key] = value;
            }

            key = null;
        }

        return new PlistDictionary(items);
    }

    private static PlistArray ParseArray(XElement element, int depth)
    {
        var items = new List<PlistNode>();
        foreach (var child in element.Elements())
        {
            var value = ParseElement(child, depth + 1);
            if (value is not null)
            {
                items.Add(value);
            }
        }

        return new PlistArray(items);
    }
}

internal static class PlistDictionaryExtensions
{
    public static string? GetString(this PlistDictionary? dictionary, string key) =>
        dictionary is not null && dictionary.Items.TryGetValue(key, out var node) && node is PlistString text
            ? text.Value
            : null;

    public static bool? GetBoolean(this PlistDictionary? dictionary, string key) =>
        dictionary is not null && dictionary.Items.TryGetValue(key, out var node) && node is PlistBoolean boolean
            ? boolean.Value
            : null;
}
