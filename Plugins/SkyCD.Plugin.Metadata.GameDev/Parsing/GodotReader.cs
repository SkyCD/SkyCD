using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Godot text resources (<c>.tscn</c>, <c>.escn</c>, <c>.tres</c>, <c>.import</c>, <c>.gdextension</c>,
/// <c>project.godot</c>) and the binary resource/package headers (<c>.res</c>, <c>.pck</c>).
/// </summary>
internal static partial class GodotReader
{
    internal static GameDevFacts ReadText(string extension, string text, bool truncated)
    {
        return extension switch
        {
            ".godot" => ReadProject(text, truncated),
            ".import" => ReadImport(text, truncated),
            ".gdextension" => ReadGdextension(text, truncated),
            ".tres" => ReadResourceText(text, truncated),
            _ => ReadSceneText(text, truncated)
        };
    }

    internal static GameDevFacts? ReadBinary(string extension, byte[] data, bool truncated)
    {
        if (extension == ".pck")
        {
            return ReadPack(data, truncated);
        }

        if (data.Length < 8)
        {
            return null;
        }

        var bigEndian = GameDevSource.StartsWithAscii(data, "RSRC") || GameDevSource.StartsWithAscii(data, "RSCC");
        if (!bigEndian)
        {
            return null;
        }

        var compressed = GameDevSource.StartsWithAscii(data, "RSCC");
        var format = ReadUInt32(data, 4, bigEndian);
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = compressed ? "Godot compressed resource" : "Godot binary resource",
            FormatVersion = format.ToString(CultureInfo.InvariantCulture),
            Compiled = true,
            ScanIncomplete = truncated
        };

        if (format >= 3 && data.Length >= 24)
        {
            var major = ReadUInt32(data, 8, bigEndian);
            var minor = ReadUInt32(data, 12, bigEndian);
            var patch = ReadUInt32(data, 16, bigEndian);
            if (major is > 0 and < 100 && minor < 100 && patch < 1000)
            {
                facts.EngineVersion = $"{major}.{minor}.{patch}";
            }
        }

        return facts;
    }

    private static GameDevFacts ReadProject(string text, bool truncated)
    {
        var sections = IniSections.Parse(text);
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot project",
            Application = "Godot",
            ProjectName = sections.Get("application", "config/name")?.Trim('"'),
            FormatVersion = sections.Get("", "config_version"),
            ScanIncomplete = truncated
        };

        var features = sections.Get("application", "config/features");
        if (!string.IsNullOrWhiteSpace(features))
        {
            var match = VersionInFeatures().Match(features);
            if (match.Success)
            {
                facts.EngineVersion = match.Groups["Version"].Value;
            }
        }

        var mainScene = sections.Get("application", "run/main_scene")?.Trim('"');
        facts.ResourceCount = sections.Count("autoload");
        facts.ImportCount = sections.Count("input");
        facts.ScriptCount = sections.Count("editor_plugins");
        facts.Application = "Godot " + (sections.Get("rendering", "renderer/rendering_method")?.Trim('"')
            ?? "default");
        facts.AddPreviewRow("Format: Godot project");
        if (!string.IsNullOrWhiteSpace(mainScene))
        {
            facts.AddPreviewRow("Main scene: " + mainScene);
        }

        foreach (var name in sections.Names("autoload"))
        {
            facts.AddNameSample(name);
        }

        return facts;
    }

    private static GameDevFacts ReadSceneText(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot scene",
            Application = "Godot",
            ScanIncomplete = truncated
        };

        var header = HeaderLine().Match(text);
        if (header.Success)
        {
            facts.FormatVersion = header.Groups["Format"].Value;
            facts.ItemName = header.Groups["Uid"].Success ? header.Groups["Uid"].Value : null;
        }

        var nodeTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in NodeLine().Matches(text))
        {
            facts.NodeCount = (facts.NodeCount ?? 0) + 1;
            var name = match.Groups["Name"].Value;
            var type = match.Groups["Type"].Value;
            if (facts.NodeCount == 1)
            {
                facts.NameSamples.Clear();
                facts.AddNameSample(name);
                facts.AddPreviewRow("Root: " + name + " (" + type + ")");
                facts.ItemName ??= name;
            }
            else
            {
                facts.AddPreviewRow("Node: " + name + " (" + type + ")");
            }

            if (type.Length > 0)
            {
                nodeTypes.Add(type);
            }
        }

        facts.ClassCount = nodeTypes.Count;
        var scripts = 0;
        foreach (Match match in ExtResourceLine().Matches(text))
        {
            facts.ResourceCount = (facts.ResourceCount ?? 0) + 1;
            if (match.Groups["Type"].Value.Contains("Script", StringComparison.Ordinal))
            {
                scripts++;
            }
        }

        facts.ScriptCount = scripts;
        facts.BlockCount = GameDevSource.CountOccurrences(text, "[sub_resource ");
        facts.ChunkCount = GameDevSource.CountOccurrences(text, "[connection ");
        return facts;
    }

    private static GameDevFacts ReadResourceText(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot resource",
            Application = "Godot",
            ScanIncomplete = truncated
        };

        var header = ResourceHeaderLine().Match(text);
        if (header.Success)
        {
            facts.ItemName = header.Groups["Type"].Value;
            facts.FormatVersion = header.Groups["Format"].Value;
            facts.Application = "Godot " + header.Groups["Format"].Value;
            var scriptClass = header.Groups["ScriptClass"].Value;
            if (scriptClass.Length > 0)
            {
                facts.AddNameSample(scriptClass);
            }
        }

        var scripts = 0;
        foreach (Match match in ExtResourceLine().Matches(text))
        {
            facts.ResourceCount = (facts.ResourceCount ?? 0) + 1;
            if (match.Groups["Type"].Value.Contains("Script", StringComparison.Ordinal))
            {
                scripts++;
            }
        }

        facts.ScriptCount = scripts;
        facts.BlockCount = GameDevSource.CountOccurrences(text, "[sub_resource ");
        facts.AddPreviewRow("Resource: " + (facts.ItemName ?? "unknown"));
        return facts;
    }

    private static GameDevFacts ReadImport(string text, bool truncated)
    {
        var sections = IniSections.Parse(text);
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot import",
            Application = "Godot import",
            ItemName = sections.Get("remap", "type")?.Trim('"'),
            FormatVersion = sections.Get("remap", "importer")?.Trim('"'),
            EngineVersion = sections.Get("remap", "uid")?.Trim('"'),
            ScanIncomplete = truncated
        };

        facts.ResourceCount = sections.Count("deps");
        facts.AddPreviewRow("Importer: " + (facts.FormatVersion ?? "unknown"));
        return facts;
    }

    private static GameDevFacts ReadGdextension(string text, bool truncated)
    {
        var sections = IniSections.Parse(text);
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot GDExtension",
            Application = "Godot",
            EngineVersion = sections.Get("configuration", "compatibility_minimum")?.Trim('"'),
            FormatVersion = sections.Get("configuration", "entry_symbol")?.Trim('"'),
            ScanIncomplete = truncated,
            ImportCount = sections.Count("dependencies"),
            ResourceCount = sections.Count("libraries")
        };

        facts.AddPreviewRow("Entry: " + (facts.FormatVersion ?? "unknown"));
        return facts;
    }

    private static GameDevFacts? ReadPack(byte[] data, bool truncated)
    {
        if (data.Length < 20 || !GameDevSource.StartsWithAscii(data, "GDPC"))
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        var major = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
        var minor = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4));
        var patch = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16, 4));
        var flags = data.Length >= 24 ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20, 4)) : 0;

        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot package",
            Application = "Godot",
            FormatVersion = version.ToString(CultureInfo.InvariantCulture),
            EngineVersion = $"{major}.{minor}.{patch}",
            Compiled = true,
            ScanIncomplete = truncated
        };

        if ((flags & 1) != 0)
        {
            facts.AddPreviewRow("Directory: encrypted");
        }

        return facts;
    }

    private static uint ReadUInt32(byte[] data, int offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    [GeneratedRegex(@"^\s*\[gd_scene\s+[^\]]*?format=(?<Format>\d+)[^\]]*?(?:uid=""(?<Uid>[^""]+)"")?",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeaderLine();

    [GeneratedRegex(@"^\s*\[gd_resource\s+[^\]]*?type=""(?<Type>[^""]+)""[^\]]*?format=(?<Format>\d+)[^\]]*?(?:script_class=""(?<ScriptClass>[^""]*)"")?",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ResourceHeaderLine();

    [GeneratedRegex(@"^\s*\[node\s+[^\]]*?name=""(?<Name>[^""]*)""[^\]]*?type=""(?<Type>[^""]*)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NodeLine();

    [GeneratedRegex(@"^\s*\[ext_resource\s+[^\]]*?type=""(?<Type>[^""]*)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExtResourceLine();

    [GeneratedRegex(@"""(?<Version>\d+\.\d+(?:\.\d+)?)""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionInFeatures();
}
