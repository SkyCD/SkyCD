using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;
using System.Xml.Linq;

namespace SkyCD.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        private Dictionary<string, string> _strings = new();
        public CultureInfo CurrentCulture { get; private set; } = CultureInfo.InvariantCulture;
        public event EventHandler? LanguageChanged;

        private LocalizationService() { }

        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public void LoadLocaleFile(string path)
        {
            var actualPath = path;
            if (!File.Exists(actualPath))
            {
                var fileName = Path.GetFileName(path);
                var tryPaths = new[] {
                    Path.Combine(AppContext.BaseDirectory, fileName),
                    Path.Combine(AppContext.BaseDirectory, "Data", fileName),
                    Path.Combine(Directory.GetCurrentDirectory(), fileName),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", fileName)
                };

                actualPath = tryPaths.FirstOrDefault(File.Exists) ?? actualPath;
            }

            if (!File.Exists(actualPath))
            {
                System.Diagnostics.Debug.WriteLine($"Localization file not found: {path}");
                return;
            }

            var ext = Path.GetExtension(actualPath).ToLowerInvariant();
            if (ext == ".xlf" || ext == ".xliff")
            {
                var parsed = ParseXlfToFlat(actualPath);
                foreach (var kv in parsed)
                    _strings[kv.Key] = kv.Value;
            }
        }

        public string[] GetAvailableLanguages(string dataDir)
        {
            var list = new List<string>();
            try
            {
                // only XLIFF files are supported now
                if (Directory.Exists(dataDir))
                {
                    foreach (var f in Directory.GetFiles(dataDir, "*.xlf"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (!list.Contains(name))
                            list.Add(name);
                    }
                    foreach (var f in Directory.GetFiles(dataDir, "*.xliff"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (!list.Contains(name))
                            list.Add(name);
                    }
                }

                var baseData = Path.Combine(AppContext.BaseDirectory, "Data");
                if (Directory.Exists(baseData))
                {
                    foreach (var f in Directory.GetFiles(baseData, "*.xlf"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (!list.Contains(name))
                            list.Add(name);
                    }
                    foreach (var f in Directory.GetFiles(baseData, "*.xliff"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (!list.Contains(name))
                            list.Add(name);
                    }
                }
            }
            catch { }

            if (!list.Contains("en"))
                list.Insert(0, "en");

            return list.ToArray();
        }

        // Parse XLIFF file and return flat dictionary of keys -> source text
        private Dictionary<string, string> ParseXlfToFlat(string filePath)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Load(filePath);
                var transUnits = doc.Descendants().Where(e => e.Name.LocalName == "trans-unit");
                foreach (var tu in transUnits)
                {
                    var id = tu.Attribute("id")?.Value ?? string.Empty;
                    if (string.IsNullOrEmpty(id))
                        continue;
                    var src = tu.Elements().FirstOrDefault(e => e.Name.LocalName == "source")?.Value ?? string.Empty;
                    var key = id.Trim().ToLowerInvariant().Replace(' ', '_');
                    result[key] = src;
                }
            }
            catch { }
            return result;
        }

        public LanguageInfo[] GetAvailableLanguageInfos(string dataDir)
        {
            var list = new List<LanguageInfo>();
            try
            {
                var files = new List<string>();
                if (Directory.Exists(dataDir))
                {
                    // only consider XLIFF files for language info
                    files.AddRange(Directory.GetFiles(dataDir, "*.xlf"));
                    files.AddRange(Directory.GetFiles(dataDir, "*.xliff"));
                }

                var baseData = Path.Combine(AppContext.BaseDirectory, "Data");
                if (Directory.Exists(baseData))
                {
                    files.AddRange(Directory.GetFiles(baseData, "*.xlf"));
                    files.AddRange(Directory.GetFiles(baseData, "*.xliff"));
                }

                foreach (var f in files.Distinct())
                {
                    try
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        var code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        string? name = null;
                        string? flag = null;

                        if (ext == ".xlf" || ext == ".xliff")
                        {
                            try
                            {
                                var doc = XDocument.Load(f);
                                var fileElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "file");
                                if (fileElem != null)
                                {
                                    var tgt = fileElem.Attribute("target-language")?.Value ?? fileElem.Attribute("source-language")?.Value;
                                    if (!string.IsNullOrEmpty(tgt)) code = tgt.ToLowerInvariant();
                                    var header = fileElem.Elements().FirstOrDefault(e => e.Name.LocalName == "header");
                                    var notes = header?.Elements().Where(e => e.Name.LocalName == "note");
                                    if (notes != null)
                                    {
                                        foreach (var note in notes)
                                        {
                                            var cat = note.Attribute("category")?.Value?.ToLowerInvariant();
                                            var text = (note.Value ?? string.Empty).Trim();
                                            if (!string.IsNullOrEmpty(cat))
                                            {
                                                if (cat == "name") name = text;
                                                else if (cat == "flag") flag = text;
                                                else
                                                {
                                                    // store other note categories as meta.<category>
                                                    // not exposed in LanguageInfo now but could be used later
                                                }
                                            }
                                            else
                                            {
                                                // legacy note parsing: try "key: value" lines
                                                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                                foreach (var line in lines)
                                                {
                                                    var idx = line.IndexOf(':');
                                                    if (idx > 0)
                                                    {
                                                        var k = line.Substring(0, idx).Trim().ToLowerInvariant();
                                                        var v = line.Substring(idx + 1).Trim().Trim('"');
                                                        if (k == "name") name = v;
                                                        if (k == "flag") flag = v;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else if (ext == ".yml" || ext == ".yaml")
                        {
                            try
                            {
                                var yaml = File.ReadAllText(f);
                                var yml = new YamlStream();
                                yml.Load(new StringReader(yaml));
                                var root = yml.Documents.Count > 0 ? yml.Documents[0].RootNode as YamlMappingNode : null;
                                if (root != null && root.Children.TryGetValue(new YamlScalarNode("meta"), out var metaNode) && metaNode is YamlMappingNode metaMap)
                                {
                                    if (metaMap.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode) && nameNode is YamlScalarNode nameScalar)
                                        name = nameScalar.Value;
                                    if (metaMap.Children.TryGetValue(new YamlScalarNode("flag"), out var flagNode) && flagNode is YamlScalarNode flagScalar)
                                        flag = flagScalar.Value;
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(name))
                        {
                            try { name = new CultureInfo(code).DisplayName; } catch { name = code; }
                        }

                        var info = new LanguageInfo { Code = code, Name = name ?? code, FlagPath = flag };
                        if (!list.Any(l => l.Code == info.Code))
                            list.Add(info);
                    }
                    catch { }
                }
            }
            catch { }

            if (!list.Any(l => l.Code == "en"))
                list.Insert(0, new LanguageInfo { Code = "en", Name = "English", FlagPath = "🇬🇧" });

            return list.ToArray();
        }

        public string SetLanguage(string selectedLanguage, string dataDir)
        {
            // available languages
            var available = GetAvailableLanguages(dataDir);
            // normalize selectedLanguage: accept values like "lt", "lt.yml" or "lt.xlf"
            var lang = "en";
            if (!string.IsNullOrEmpty(selectedLanguage))
            {
                try
                {
                    lang = Path.GetFileNameWithoutExtension(selectedLanguage).ToLowerInvariant();
                }
                catch { lang = selectedLanguage.ToLowerInvariant(); }
            }

            if (!available.Contains(lang))
            {
                // fallback to first available
                lang = available.Length > 0 ? available[0] : "en";
            }

            // clear previous strings
            _strings.Clear();

            // load english first (if present) - use XLIFF
            var enPath = Path.Combine(dataDir, "en.xlf");
            if (!File.Exists(enPath))
                enPath = Path.Combine(dataDir, "en.xliff");
            LoadLocaleFile(enPath);

            if (lang != "en")
            {
                var selPath = Path.Combine(dataDir, lang + ".xlf");
                if (!File.Exists(selPath))
                    selPath = Path.Combine(dataDir, lang + ".xliff");
                LoadLocaleFile(selPath);
            }

            try
            {
                CurrentCulture = new CultureInfo(lang);
            }
            catch { }

            // notify listeners that language changed
            LanguageChanged?.Invoke(this, EventArgs.Empty);

            return lang;
        }

        private static string NormalizeKey(string k)
        {
            if (string.IsNullOrEmpty(k))
                return k;
            return k.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        }

        private Dictionary<string, string> ParseYamlToFlat(string yamlContent)
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
                // to lower, replace spaces and hyphens with underscore
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

                    // Also register a short key (last segment) as a fallback for simple lookups
                    var lastDot = normalizedPrefix.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        var shortKey = normalizedPrefix.Substring(lastDot + 1);
                        if (!result.ContainsKey(shortKey))
                            result[shortKey] = value;
                    }
                    else
                    {
                        // prefix has no dot; ensure shortKey exists (same as prefix)
                        if (!result.ContainsKey(normalizedPrefix))
                            result[normalizedPrefix] = value;
                    }
                }
                else if (node is YamlSequenceNode seq)
                {
                    // flatten sequence by index
                    for (int i = 0; i < seq.Children.Count; i++)
                    {
                        Recurse(seq.Children[i], prefix + "." + i);
                    }
                }
            }

            Recurse(root, string.Empty);
            return result;
        }

        public void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;
        }

        public string T(string key)
        {
            var norm = NormalizeKey(key);
            if (_strings.TryGetValue(norm, out var v))
                return v;
            return key;
        }
    }
}
