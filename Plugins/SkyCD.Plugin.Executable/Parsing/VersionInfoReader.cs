using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkyCD.Plugin.Executable.Parsing;

/// <summary>
/// Reads the string and fixed entries of an <c>RT_VERSION</c> resource.
/// </summary>
/// <remarks>
/// The resource is a chain of <c>VS_VERSION_INFO</c>, <c>StringFileInfo</c>, string table and string nodes, each
/// with a six-byte header, a zero-terminated UTF-16 key, padding to a four-byte boundary and a value. The
/// walker validates every declared size against the resource before it uses it, so a crafted resource can only
/// lose its own values. Fixed-file-info fields are the fallback when a localized string table is absent, which
/// is common in older programs.
/// </remarks>
internal static class VersionInfoReader
{
    /// <summary>String keys the plugin publishes, mapped to their schema field names.</summary>
    private static readonly string[] WantedKeys =
    [
        "FileVersion", "ProductVersion", "CompanyName", "ProductName", "FileDescription", "OriginalFilename",
        "InternalName", "LegalCopyright"
    ];

    private const int FixedFileInfoSize = 52;
    private const int MaximumValueCharacters = 1024;

    /// <summary>
    /// Reads the values of a version resource. A damaged resource returns no values instead of throwing, so a
    /// single bad string table cannot hide the rest of the metadata.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(ReadOnlySpan<byte> resource)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (resource.Length < 6 + (2 * 16) || BinaryPrimitives.ReadUInt16LittleEndian(resource) < 6)
        {
            return values;
        }

        var node = ReadNode(resource, 0, resource.Length, valueLengthInWords: false);
        if (node is not { } root || !root.Key.Equals("VS_VERSION_INFO", StringComparison.Ordinal))
        {
            return values;
        }

        ReadFixedFileInfo(root.Value, values);
        for (var index = 0; index < root.Children.Count; index++)
        {
            var child = root.Children[index];
            if (!child.Key.Equals("StringFileInfo", StringComparison.Ordinal))
            {
                continue;
            }

            for (var tableIndex = 0; tableIndex < child.Children.Count; tableIndex++)
            {
                var table = child.Children[tableIndex];
                for (var valueIndex = 0; valueIndex < table.Children.Count; valueIndex++)
                {
                    var value = table.Children[valueIndex];
                    if (value.ValueText is { Length: > 0 } text && !values.ContainsKey(value.Key))
                    {
                        values[value.Key] = text;
                    }
                }
            }
        }

        return values;
    }

    private static void ReadFixedFileInfo(ReadOnlySpan<byte> value, Dictionary<string, string> values)
    {
        if (value.Length < FixedFileInfoSize || BinaryPrimitives.ReadUInt32LittleEndian(value) != 0xFEEF04BD)
        {
            return;
        }

        // The high and low 32-bit words of the version integers hold the dotted parts in that order.
        values.TryAdd("FileVersion", FormatVersion(
            BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(12, 4))));
        values.TryAdd("ProductVersion", FormatVersion(
            BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(20, 4))));
    }

    private static string FormatVersion(uint high, uint low)
    {
        var major = high >> 16;
        var minor = high & 0xFFFF;
        var build = low >> 16;
        var revision = low & 0xFFFF;
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{build}.{revision}");
    }

    /// <summary>A version node: its key, its own value bytes and the nodes nested inside it.</summary>
    private sealed record VersionNode(string Key, byte[] Value, IReadOnlyList<VersionNode> Children)
    {
        public string? ValueText => ReadValueText(Value);
    }

    private static VersionNode? ReadNode(ReadOnlySpan<byte> data, int start, int end, bool valueLengthInWords)
    {
        if (start < 0 || end > data.Length || end - start < 6)
        {
            return null;
        }

        var node = data.Slice(start, end - start);
        var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(node);
        var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(node.Slice(2, 2));
        if (declaredLength < 6 || declaredLength > node.Length)
        {
            return null;
        }

        var keyEnd = 6;
        while (keyEnd + 1 < declaredLength && (node[keyEnd] != 0 || node[keyEnd + 1] != 0))
        {
            keyEnd += 2;
        }

        var key = Encoding.Unicode.GetString(node.Slice(6, keyEnd - 6));
        var valueStart = Align4(keyEnd + 2);

        // Only the fixed block of VS_VERSION_INFO declares its length in bytes; every string node declares it in
        // UTF-16 words, so reading the same number of bytes would cut the value in half.
        var valueBytes = valueLengthInWords ? Math.Min(declaredLength, valueLength * 2) : valueLength;
        var valueEnd = Math.Min(declaredLength, valueStart + valueBytes);
        var value = valueStart <= valueEnd ? node.Slice(valueStart, valueEnd - valueStart).ToArray() : [];

        var children = new List<VersionNode>();
        var childStart = Align4(valueEnd);
        while (childStart + 6 <= declaredLength)
        {
            var childLength = BinaryPrimitives.ReadUInt16LittleEndian(node.Slice(childStart, 2));
            if (childLength < 6 || childStart + childLength > declaredLength)
            {
                break;
            }

            var child = ReadNode(node[..declaredLength], childStart, childStart + childLength,
                valueLengthInWords: true);
            if (child is not null)
            {
                children.Add(child);
            }

            childStart = Align4(childStart + childLength);
        }

        return new VersionNode(key, value, children);
    }

    private static string? ReadValueText(ReadOnlySpan<byte> value)
    {
        if (value.Length < 2)
        {
            return null;
        }

        var length = value.Length & ~1;
        while (length >= 2 && value[length - 1] == 0 && value[length - 2] == 0)
        {
            length -= 2;
        }

        var text = Encoding.Unicode.GetString(value[..length]).Trim();
        return text.Length is > 0 and <= MaximumValueCharacters ? text : null;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    /// <summary>The string keys the plugin reads from a string table, exposed for documentation and tests.</summary>
    public static IReadOnlyList<string> PublishedKeys => WantedKeys;
}
