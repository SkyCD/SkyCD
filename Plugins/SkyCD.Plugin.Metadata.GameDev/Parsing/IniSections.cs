using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Minimal INI reader used by Godot text resources. Sections keep their first occurrence, keys keep the last
/// value, and values keep any surrounding quotes for the caller to strip.
/// </summary>
internal sealed class IniSections
{
    private readonly Dictionary<string, Dictionary<string, string>> sections =
        new(StringComparer.OrdinalIgnoreCase);

    private IniSections()
    {
    }

    /// <summary>Sections in document order, including the implicit empty section for leading keys.</summary>
    internal IEnumerable<string> SectionNames => sections.Keys;

    internal static IniSections Parse(string text)
    {
        var result = new IniSections();
        var current = result.GetOrAdd(string.Empty);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                current = result.GetOrAdd(line[1..^1].Trim());
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return result;
    }

    internal string? Get(string section, string key) =>
        sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    internal int Count(string section) => sections.TryGetValue(section, out var values) ? values.Count : 0;

    internal IEnumerable<string> Names(string section) =>
        sections.TryGetValue(section, out var values) ? values.Keys : [];

    private Dictionary<string, string> GetOrAdd(string section)
    {
        if (!sections.TryGetValue(section, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sections[section] = values;
        }

        return values;
    }
}
