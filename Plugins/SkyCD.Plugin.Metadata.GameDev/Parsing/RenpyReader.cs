using System;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Ren'Py scripts, compiled script slot tables and RPA archive headers. The RPA index is not unpickled, so
/// only the version, index offset and key are published.
/// </summary>
internal static partial class RenpyReader
{
    internal static GameDevFacts ReadScript(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Ren'Py",
            Format = "Ren'Py script",
            Application = "Ren'Py",
            ScanIncomplete = truncated
        };

        long labels = 0;
        long characters = 0;
        long lines = 0;
        foreach (var raw in text.Split('\n'))
        {
            lines++;
            var line = raw.TrimStart();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var label = LabelLine().Match(line);
            if (label.Success)
            {
                labels++;
                facts.AddNameSample(label.Groups["Name"].Value);
                facts.AddPreviewRow("Label: " + label.Groups["Name"].Value);
            }

            if (line.Contains("Character(", StringComparison.Ordinal))
            {
                characters++;
            }
        }

        facts.LabelCount = labels;
        facts.CharacterCount = characters;
        facts.FileCount = lines;
        return facts;
    }

    internal static GameDevFacts? ReadCompiled(byte[] data, bool truncated)
    {
        if (!GameDevSource.StartsWithAscii(data, "RENPY RPC2"))
        {
            return new GameDevFacts
            {
                Family = "Ren'Py",
                Format = "Ren'Py compiled script",
                Application = "Ren'Py",
                Valid = false
            };
        }

        var facts = new GameDevFacts
        {
            Family = "Ren'Py",
            Format = "Ren'Py compiled script",
            Application = "Ren'Py",
            FormatVersion = "RPC2",
            Compiled = true,
            Valid = true,
            ScanIncomplete = truncated
        };

        long slots = 0;
        var offset = 10;
        while (offset + 12 <= data.Length && slots < 1024)
        {
            var slot = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            if (slot == 0)
            {
                break;
            }

            var start = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8, 4));
            if (start > data.Length || length > data.Length - start)
            {
                facts.ScanIncomplete = true;
                break;
            }

            slots++;
            offset += 12;
        }

        facts.ResourceCount = slots;
        return facts;
    }

    internal static GameDevFacts? ReadArchive(byte[] data)
    {
        var text = GameDevSource.DecodeText(data.AsSpan(0, Math.Min(data.Length, 128)));
        var match = ArchiveHeader().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return new GameDevFacts
        {
            Family = "Ren'Py",
            Format = "Ren'Py archive",
            Application = "Ren'Py",
            FormatVersion = match.Groups["Version"].Value,
            ItemName = "index 0x" + match.Groups["Offset"].Value
        };
    }

    [GeneratedRegex(@"^label\s+(?<Name>[\w.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LabelLine();

    [GeneratedRegex(@"^RPA-(?<Version>\d+\.\d+)\s+(?<Offset>[0-9a-fA-F]+)(?:\s+(?<Key>[0-9a-fA-F]+))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveHeader();
}
