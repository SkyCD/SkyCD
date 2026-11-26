using System;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads CRYENGINE project JSON and character parameter XML without opening any referenced asset.</summary>
internal static class CryEngineReader
{
    internal static GameDevFacts? ReadProject(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var facts = new GameDevFacts
            {
                Family = "CRYENGINE",
                Format = "CRYENGINE project",
                Application = "CRYENGINE",
                ProjectName = GameDevDocuments.GetString(root, "name")
                    ?? GameDevDocuments.GetString(root, "Name"),
                FormatVersion = GameDevDocuments.GetString(root, "version"),
                ScanIncomplete = truncated,
                ResourceCount = GameDevDocuments.CountArray(root, "require")
            };

            if (root.TryGetProperty("require", out var modules)
                && modules.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var module in modules.EnumerateArray())
                {
                    var name = GameDevDocuments.GetString(module, "name");
                    facts.AddNameSample(name);
                    facts.AddPreviewRow("Module: " + name);
                }
            }

            return facts;
        }
    }

    internal static GameDevFacts? ReadCharacterParams(string text, bool truncated)
    {
        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            if (!GameDevDocuments.ReadRootElement(reader))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "CRYENGINE",
                Format = "CRYENGINE character parameters",
                Application = "CRYENGINE",
                ScanIncomplete = truncated
            };

            var depth = reader.Depth;
            long count = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element)
                {
                    var name = reader.GetAttribute("name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        count++;
                        facts.AddNameSample(name);
                    }
                }
            }

            facts.NodeCount = count;
            return facts;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
