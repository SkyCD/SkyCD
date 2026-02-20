using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;

namespace SkyCD.Tools
{
    public static class YamlToXliffConverter
    {
        // Convert all .yml/.yaml files in dataDir to .xlf files next to them.
        public static void ConvertAllYamlToXliff(string dataDir)
        {
            if (!Directory.Exists(dataDir))
                return;

            var files = Directory.GetFiles(dataDir, "*.yml").Concat(Directory.GetFiles(dataDir, "*.yaml"));
            foreach (var f in files)
            {
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(f);
                    var outPath = Path.Combine(Path.GetDirectoryName(f) ?? dataDir, baseName + ".xlf");
                    ConvertYamlToXliff(f, outPath);
                }
                catch { }
            }
        }

        public static void ConvertYamlToXliff(string yamlPath, string xlfPath)
        {
            if (!File.Exists(yamlPath))
                return;

            var yamlText = File.ReadAllText(yamlPath);
            var input = new StringReader(yamlText);
            var yaml = new YamlStream();
            try
            {
                yaml.Load(input);
            }
            catch
            {
                return; // invalid yaml
            }

            var root = yaml.Documents.Count > 0 ? yaml.Documents[0].RootNode as YamlMappingNode : null;
            var metaName = (string?)null;
            var metaFlag = (string?)null;

            if (root != null && root.Children.TryGetValue(new YamlScalarNode("meta"), out var metaNode) && metaNode is YamlMappingNode metaMap)
            {
                if (metaMap.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode) && nameNode is YamlScalarNode nameScalar)
                    metaName = nameScalar.Value;
                if (metaMap.Children.TryGetValue(new YamlScalarNode("flag"), out var flagNode) && flagNode is YamlScalarNode flagScalar)
                    metaFlag = flagScalar.Value;
            }

            var flattened = ParseYamlToFlat(yamlText);

            var lang = Path.GetFileNameWithoutExtension(yamlPath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(metaName))
            {
                try { metaName = new System.Globalization.CultureInfo(lang).DisplayName; } catch { metaName = lang; }
            }

            // create header notes: prefer category attribute notes (one per meta entry)
            var headerNotes = new List<XElement>();
            if (!string.IsNullOrEmpty(metaName))
                headerNotes.Add(new XElement("note", new XAttribute("category", "name"), metaName));
            if (!string.IsNullOrEmpty(metaFlag))
                headerNotes.Add(new XElement("note", new XAttribute("category", "flag"), metaFlag));
            // also keep legacy combined note for compatibility
            headerNotes.Add(new XElement("note", $"name: \"{metaName}\"\nflag: \"{metaFlag}\""));

            var xliff = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("xliff",
                    new XAttribute("version", "1.2"),
                    new XElement("file",
                        new XAttribute("source-language", lang),
                        new XAttribute("target-language", lang),
                        new XAttribute("datatype", "plaintext"),
                        new XElement("header", headerNotes.Cast<object>()),
                        new XElement("body",
                            flattened.Select(kv =>
                                new XElement("trans-unit",
                                    new XAttribute("id", kv.Key),
                                    new XElement("source", kv.Value ?? string.Empty)
                                )
                            )
                        )
                    )
                )
            );

            try
            {
                xliff.Save(xlfPath);
            }
            catch { }
        }

        private static Dictionary<string, string> ParseYamlToFlat(string yamlContent)
        {
            var result = new Dictionary<string, string>();
            var input = new StringReader(yamlContent);
            var yaml = new YamlStream();
            yaml.Load(input);
            if (yaml.Documents.Count == 0)
                return result;

            var root = yaml.Documents[0].RootNode as YamlMappingNode;
            if (root == null)
                return result;

            string NormalizeKey(string k)
            {
                if (string.IsNullOrEmpty(k))
                    return k;
                return k.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
            }

            void Recurse(YamlNode node, string prefix)
            {
                if (node is YamlMappingNode map)
                {
                    foreach (var entry in map.Children)
                    {
                        var key = ((YamlScalarNode)entry.Key).Value ?? string.Empty;
                        key = NormalizeKey(key);
                        var childPrefix = string.IsNullOrEmpty(prefix) ? key : prefix + "." + key;
                        Recurse(entry.Value, childPrefix);
                    }
                }
                else if (node is YamlScalarNode scalar)
                {
                    var value = scalar.Value ?? string.Empty;
                    var normalizedPrefix = NormalizeKey(prefix);
                    result[normalizedPrefix] = value;
                }
                else if (node is YamlSequenceNode seq)
                {
                    for (int i = 0; i < seq.Children.Count; i++)
                    {
                        Recurse(seq.Children[i], prefix + "." + i);
                    }
                }
            }

            Recurse(root, string.Empty);
            return result;
        }
    }
}
