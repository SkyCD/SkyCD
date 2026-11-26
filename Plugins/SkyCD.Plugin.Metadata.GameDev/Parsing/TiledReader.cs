using System;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Tiled maps and worlds. XML maps use bounded attribute reads; JSON maps use the same keys. External
/// tilesets, templates and images are recorded by name only and never opened.
/// </summary>
internal static class TiledReader
{
    internal static GameDevFacts? ReadXml(string extension, string text, bool truncated)
    {
        GameDevFacts facts;
        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            if (!GameDevDocuments.ReadRootElement(reader))
            {
                return null;
            }

            var root = reader.LocalName;
            facts = new GameDevFacts
            {
                Family = "Tiled",
                Format = extension == ".world" ? "Tiled world" : "Tiled map",
                Application = "Tiled",
                FormatVersion = reader.GetAttribute("tiledversion") ?? reader.GetAttribute("version"),
                ScanIncomplete = truncated,
                MapWidth = ParseLong(reader.GetAttribute("width")),
                MapHeight = ParseLong(reader.GetAttribute("height")),
                TileWidth = ParseLong(reader.GetAttribute("tilewidth")),
                TileHeight = ParseLong(reader.GetAttribute("tileheight"))
            };

            if (root == "world")
            {
                ReadWorld(reader, facts);
                return facts;
            }

            if (root != "map")
            {
                return null;
            }

            var depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "layer":
                        facts.LayerCount = (facts.LayerCount ?? 0) + 1;
                        facts.AddNameSample(reader.GetAttribute("name"));
                        facts.AddPreviewRow("Layer: " + reader.GetAttribute("name"));
                        break;
                    case "objectgroup":
                        facts.ObjectGroupCount = (facts.ObjectGroupCount ?? 0) + 1;
                        facts.LayerCount = (facts.LayerCount ?? 0) + 1;
                        facts.AddNameSample(reader.GetAttribute("name"));
                        break;
                    case "imagelayer":
                        facts.LayerCount = (facts.LayerCount ?? 0) + 1;
                        facts.AddNameSample(reader.GetAttribute("name"));
                        break;
                    case "group":
                        facts.LayerCount = (facts.LayerCount ?? 0) + 1;
                        break;
                    case "object":
                        facts.ObjectCount = (facts.ObjectCount ?? 0) + 1;
                        facts.AddNameSample(reader.GetAttribute("name"));
                        facts.AddPreviewRow("Object: " + reader.GetAttribute("name"));
                        break;
                    case "tileset":
                        facts.ResourceCount = (facts.ResourceCount ?? 0) + 1;
                        facts.AddTilesetName(reader.GetAttribute("name") ?? reader.GetAttribute("source"));
                        break;
                    case "property":
                        facts.BlockCount = (facts.BlockCount ?? 0) + 1;
                        break;
                }
            }

            facts.TileLayerCount = facts.LayerCount;
            return facts;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    internal static GameDevFacts? ReadJson(string extension, string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Tiled",
                Format = "Tiled map",
                Application = "Tiled",
                FormatVersion = GameDevDocuments.GetString(root, "tiledversion")
                    ?? GameDevDocuments.GetString(root, "version"),
                ScanIncomplete = truncated,
                MapWidth = GameDevDocuments.GetInteger(root, "width"),
                MapHeight = GameDevDocuments.GetInteger(root, "height"),
                TileWidth = GameDevDocuments.GetInteger(root, "tilewidth"),
                TileHeight = GameDevDocuments.GetInteger(root, "tileheight")
            };

            CountLayers(root, facts);
            return facts;
        }
    }

    private static void ReadWorld(XmlReader reader, GameDevFacts facts)
    {
        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "map")
            {
                facts.SceneCount = (facts.SceneCount ?? 0) + 1;
                var file = reader.GetAttribute("filename") ?? reader.GetAttribute("fileName");
                facts.AddNameSample(file);
                facts.AddPreviewRow("Map: " + file);
            }
        }
    }

    private static void CountLayers(System.Text.Json.JsonElement map, GameDevFacts facts)
    {
        if (!map.TryGetProperty("layers", out var layers) || layers.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        foreach (var layer in layers.EnumerateArray())
        {
            facts.LayerCount = (facts.LayerCount ?? 0) + 1;
            var type = GameDevDocuments.GetString(layer, "type");
            var name = GameDevDocuments.GetString(layer, "name");
            facts.AddNameSample(name);
            facts.AddPreviewRow("Layer: " + name);
            switch (type)
            {
                case "objectgroup":
                    facts.ObjectGroupCount = (facts.ObjectGroupCount ?? 0) + 1;
                    facts.ObjectCount = (facts.ObjectCount ?? 0)
                        + (GameDevDocuments.CountArray(layer, "objects") ?? 0);
                    break;
                case "group":
                    CountLayers(layer, facts);
                    break;
            }
        }

        if (map.TryGetProperty("tilesets", out var tilesets) && tilesets.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var tileset in tilesets.EnumerateArray())
            {
                facts.ResourceCount = (facts.ResourceCount ?? 0) + 1;
                facts.AddTilesetName(GameDevDocuments.GetString(tileset, "name")
                    ?? GameDevDocuments.GetString(tileset, "source"));
            }
        }

        facts.TileLayerCount = facts.LayerCount;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, out var parsed) ? parsed : null;
}
