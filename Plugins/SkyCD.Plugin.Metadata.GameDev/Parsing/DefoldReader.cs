using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Defold collection, GUI, tilemap, atlas and sprite documents. They are Protobuf text; the reader takes
/// message ids, component types and tile counts without resolving referenced resources.
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

        return facts;
    }

    private static string FormatName(string extension) => extension switch
    {
        ".collection" => "Defold collection",
        ".gui" => "Defold GUI",
        ".tilemap" => "Defold tilemap",
        ".atlas" => "Defold atlas",
        ".sprite" => "Defold sprite",
        _ => "Defold resource"
    };

    [GeneratedRegex(@"^\s*id:\s*""(?<Id>[^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex IdLine();

    [GeneratedRegex(@"^\s*type:\s*(?<Type>[\w.]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TypeLine();
}
