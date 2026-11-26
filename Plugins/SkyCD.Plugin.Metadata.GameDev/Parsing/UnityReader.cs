using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Unity YAML documents by their class-id anchors and named fields. The file must carry the Unity YAML
/// header; a file with the same extension but foreign content publishes <c>Valid=false</c> and no numbers.
/// </summary>
internal static partial class UnityReader
{
    internal static GameDevFacts Read(string extension, string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Unity",
            Format = FormatName(extension),
            Application = "Unity",
            ScanIncomplete = truncated
        };

        if (!text.Contains("%YAML 1.1", StringComparison.Ordinal)
            || !text.Contains("tag:unity3d.com", StringComparison.Ordinal))
        {
            facts.Valid = false;
            return facts;
        }

        facts.FormatVersion = "1.1";
        facts.ClassCount = GameDevSource.CountOccurrences(text, "--- !u!");
        var gameObjects = 0;
        long nodes = 0;
        foreach (Match match in DocumentLine().Matches(text))
        {
            var classId = match.Groups["Class"].Value;
            switch (classId)
            {
                case "1":
                    gameObjects++;
                    break;
                case "4":
                    nodes++;
                    break;
            }
        }

        facts.EntityCount = gameObjects;
        facts.NodeCount = nodes;
        facts.ScriptCount = GameDevSource.CountOccurrences(text, "!u!114 &");

        var first = NameLine().Match(text);
        var firstName = first.Success ? first.Groups["Name"].Value.Trim() : string.Empty;
        if (firstName.Length > 0)
        {
            facts.ItemName = firstName;
        }

        var scripts = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ScriptLine().Matches(text))
        {
            if (match.Groups["Guid"].Value.Length > 0)
            {
                scripts.Add(match.Groups["Guid"].Value);
            }
        }

        facts.ImportCount = scripts.Count;
        var guids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in GuidLine().Matches(text))
        {
            guids.Add(match.Groups["Guid"].Value);
        }

        facts.ResourceCount = guids.Count;

        foreach (Match match in NameLine().Matches(text))
        {
            var name = match.Groups["Name"].Value.Trim();
            if (name.Length > 0)
            {
                facts.AddNameSample(name);
                facts.AddPreviewRow(name);
            }
        }

        return facts;
    }

    internal static GameDevFacts ReadMeta(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Unity",
            Format = "Unity meta",
            Application = "Unity",
            FormatVersion = FileFormatVersionLine().Match(text) is { Success: true } version
                ? version.Groups["Version"].Value
                : null,
            MetaGuid = GuidLine().Match(text) is { Success: true } guid ? guid.Groups["Guid"].Value : null,
            ScanIncomplete = truncated
        };

        var importer = ImporterLine().Match(text);
        if (importer.Success)
        {
            facts.ImporterType = importer.Groups["Importer"].Value;
            facts.AddPreviewRow("Importer: " + facts.ImporterType);
        }

        foreach (Match setting in ImporterSetting().Matches(text))
        {
            facts.AddPreviewRow(setting.Groups["Key"].Value + ": " + setting.Groups["Value"].Value.Trim());
        }

        return facts;
    }

    private static string FormatName(string extension) => extension switch
    {
        ".unity" => "Unity scene",
        ".prefab" => "Unity prefab",
        ".scene" => "Unity scene",
        ".asset" => "Unity asset",
        ".mat" => "Unity material",
        ".anim" => "Unity animation",
        ".controller" => "Unity animator controller",
        ".overrideController" => "Unity override controller",
        ".physicMaterial" => "Unity physics material",
        _ => "Unity YAML"
    };

    [GeneratedRegex(@"^--- !u!(?<Class>\d+) &", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentLine();

    [GeneratedRegex(@"^\s*m_Name:\s*(?<Name>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NameLine();

    [GeneratedRegex(@"m_Script:\s*\{[^}]*guid:\s*(?<Guid>[0-9a-fA-F]{32})",
        RegexOptions.CultureInvariant)]
    private static partial Regex ScriptLine();

    [GeneratedRegex(@"guid:\s*(?<Guid>[0-9a-fA-F]{32})", RegexOptions.CultureInvariant)]
    private static partial Regex GuidLine();

    [GeneratedRegex(@"^fileFormatVersion:\s*(?<Version>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FileFormatVersionLine();

    [GeneratedRegex(@"^(?<Importer>[A-Za-z]+Importer):\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ImporterLine();

    [GeneratedRegex(@"^\s{2}(?<Key>(?:maxTextureSize|textureType|spriteMode|spritePixelsToUnits|modelScaleFactor|audioLoadType)):\s*(?<Value>\S+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ImporterSetting();
}
