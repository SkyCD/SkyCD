using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Defold collection, GUI, tilemap, atlas, sprite, material and game-object documents, plus the
/// <c>game.project</c> INI descriptor. Protobuf-text documents yield message ids, component types and tile
/// counts without resolving referenced resources.
/// </summary>
internal static partial class DefoldReader
{
    internal static GameDevFacts Read(string extension, string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Defold",
            Format = FormatName(extension),
            Application = "Defold",
            ScanIncomplete = truncated
        };

        long ids = 0;
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in IdLine().Matches(text))
        {
            ids++;
            facts.AddNameSample(match.Groups["Id"].Value);
            facts.AddPreviewRow("Id: " + match.Groups["Id"].Value);
        }

        foreach (Match match in TypeLine().Matches(text))
        {
            types.Add(match.Groups["Type"].Value);
        }

        facts.ResourceCount = ids;
        facts.ClassCount = types.Count;
        facts.BlockCount = GameDevSource.CountOccurrences(text, "embedded_instances {");
        facts.ChunkCount = GameDevSource.CountOccurrences(text, "animations {");

        if (extension == ".tilemap")
        {
            facts.TileLayerCount = GameDevSource.CountOccurrences(text, "tile_set:");
        }

        if (extension == ".atlas")
        {
            facts.SceneCount = GameDevSource.CountOccurrences(text, "image:");
        }

        if (extension == ".material")
        {
            facts.ItemName = NameLine().Match(text) is { Success: true } name ? name.Groups["Name"].Value : null;
            facts.ScriptCount = (text.Contains("fragment_program:", StringComparison.Ordinal) ? 1 : 0)
                + (text.Contains("vertex_program:", StringComparison.Ordinal) ? 1 : 0);
            facts.ResourceCount = GameDevSource.CountOccurrences(text, "samplers {");
            facts.AddPreviewRow("Material: " + (facts.ItemName ?? "unknown"));
        }

        return facts;
    }

    internal static GameDevFacts? ReadProject(string text, bool truncated)
    {
        var sections = IniSections.Parse(text);
        var title = sections.Get("project", "title")?.Trim('"');
        var version = sections.Get("project", "version")?.Trim('"');
        if (title is null && version is null)
        {
            return null;
        }

        var facts = new GameDevFacts
        {
            Family = "Defold",
            Format = "Defold project",
            Application = "Defold",
            ProjectName = title,
            FormatVersion = version,
            ScanIncomplete = truncated
        };

        long dependencies = 0;
        foreach (var key in sections.Names("project"))
        {
            if (key.StartsWith("dependencies", StringComparison.OrdinalIgnoreCase))
            {
                dependencies++;
            }
        }

        facts.ResourceCount = dependencies;
        facts.AddPreviewRow("Title: " + (title ?? "unknown"));
        facts.AddPreviewRow("Version: " + (version ?? "unknown"));
        facts.AddPreviewRow("Publisher: " + (sections.Get("project", "publisher")?.Trim('"') ?? "unknown"));
        return facts;
    }

    private static string FormatName(string extension) => extension switch
    {
        ".collection" => "Defold collection",
        ".gui" => "Defold GUI",
        ".tilemap" => "Defold tilemap",
        ".atlas" => "Defold atlas",
        ".sprite" => "Defold sprite",
        ".go" => "Defold game object",
        ".material" => "Defold material",
        _ => "Defold resource"
    };

    [GeneratedRegex(@"^\s*id:\s*""(?<Id>[^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex IdLine();

    [GeneratedRegex(@"^\s*type:\s*(?<Type>[\w.]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TypeLine();

    [GeneratedRegex(@"^\s*name:\s*""(?<Name>[^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NameLine();
}
