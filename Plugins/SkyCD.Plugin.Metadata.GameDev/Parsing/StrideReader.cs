using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Stride asset YAML by its first-line type tag, id and serializer version. Component and entity names are
/// taken from tagged entries; no referenced asset is opened.
/// </summary>
internal static partial class StrideReader
{
    internal static GameDevFacts? Read(string extension, string text, bool truncated)
    {
        var tag = FirstTag().Match(text);
        if (!tag.Success)
        {
            return null;
        }

        var facts = new GameDevFacts
        {
            Family = "Stride",
            Format = FormatName(extension, tag.Groups["Tag"].Value),
            Application = "Stride",
            FormatVersion = SerializerVersion().Match(text) is { Success: true } serializer
                ? serializer.Groups["Version"].Value
                : null,
            ItemName = GuidLine().Match(text) is { Success: true } id ? id.Groups["Id"].Value : null,
            ScanIncomplete = truncated
        };

        var components = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TaggedLine().Matches(text))
        {
            components.Add(match.Groups["Tag"].Value);
        }

        facts.ClassCount = components.Count;
        facts.ResourceCount = GameDevSource.CountOccurrences(text, "- !");
        foreach (Match match in NameLine().Matches(text))
        {
            var name = match.Groups["Name"].Value;
            facts.AddNameSample(name);
            facts.AddPreviewRow("Entity: " + name);
        }

        facts.AddPreviewRow("Asset: " + facts.Format);
        return facts;
    }

    private static string FormatName(string extension, string tag) => extension switch
    {
        ".sdscene" => "Stride scene",
        ".sdprefab" => "Stride prefab",
        ".sdmat" => "Stride material",
        ".sdtex" => "Stride texture",
        _ => tag
    };

    [GeneratedRegex(@"\A\s*!?(?<Tag>[A-Za-z][\w.]*)", RegexOptions.CultureInvariant)]
    private static partial Regex FirstTag();

    [GeneratedRegex(@"^\s*SerializerVersion:\s*(?<Version>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SerializerVersion();

    [GeneratedRegex(@"^\s*Id:\s*(?<Id>[0-9a-fA-F-]{32,36})", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GuidLine();

    [GeneratedRegex(@"^\s*(?:-\s*)?!(?<Tag>[A-Za-z][\w.]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TaggedLine();

    [GeneratedRegex(@"^\s*Name:\s*(?<Name>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NameLine();
}
