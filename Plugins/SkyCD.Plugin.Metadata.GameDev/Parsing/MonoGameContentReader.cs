using System;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads a MonoGame content pipeline <c>.mgcb</c> project as line-oriented directives.</summary>
internal static class MonoGameContentReader
{
    internal static GameDevFacts Read(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "MonoGame",
            Format = "MGCB content project",
            Application = "MonoGame content pipeline",
            ScanIncomplete = truncated
        };

        long builds = 0;
        long compiles = 0;
        long references = 0;
        long copies = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var directive = colon > 0 ? line[1..colon] : line;
            var value = colon > 0 ? line[(colon + 1)..].Trim() : string.Empty;
            switch (directive)
            {
                case "platform":
                    facts.EngineVersion = value;
                    break;
                case "profile":
                    facts.FormatVersion = value;
                    break;
                case "build":
                    builds++;
                    facts.AddNameSample(System.IO.Path.GetFileName(value));
                    facts.AddPreviewRow("Build: " + value);
                    break;
                case "compile":
                    compiles++;
                    break;
                case "reference":
                    references++;
                    break;
                case "copy":
                    copies++;
                    break;
            }
        }

        facts.ResourceCount = builds;
        facts.ScriptCount = compiles;
        facts.ImportCount = references;
        facts.FileCount = copies;
        return facts;
    }
}
