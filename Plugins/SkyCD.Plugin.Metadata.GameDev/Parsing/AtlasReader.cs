using System;
using System.Collections.Generic;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads libGDX/Phaser/Spine <c>.atlas</c> pages and regions. Pages are page image names, regions are the
/// unindented region identifiers that follow the page's key block; no page image is opened.
/// </summary>
internal static class AtlasReader
{
    internal static GameDevFacts Read(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "libGDX",
            Format = "Texture atlas",
            Application = "libGDX atlas",
            ScanIncomplete = truncated
        };

        long pages = 0;
        var pendingKeyBlock = false;
        var regions = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var indented = line[0] is ' ' or '\t';
            var trimmed = line.Trim();
            if (!indented)
            {
                if (trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    pages++;
                    pendingKeyBlock = true;
                    facts.AddNameSample(trimmed);
                    continue;
                }

                if (!trimmed.Contains(':', StringComparison.Ordinal) && !trimmed.StartsWith(';'))
                {
                    regions.Add(trimmed);
                    facts.AddNameSample(trimmed);
                    facts.AddPreviewRow("Region: " + trimmed);
                }

                continue;
            }

            if (!pendingKeyBlock)
            {
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            switch (key)
            {
                case "format":
                    facts.FormatVersion ??= value;
                    break;
                case "filter":
                    facts.EngineVersion ??= value;
                    break;
                case "size":
                    if (TrySplit(value, out var width, out var height))
                    {
                        facts.ImageWidth ??= width;
                        facts.ImageHeight ??= height;
                    }

                    break;
            }
        }

        facts.FileCount = pages;
        facts.ObjectCount = regions.Count;
        facts.ClassCount = regions.Count;
        return facts;
    }

    private static bool TrySplit(string value, out long width, out long height)
    {
        width = 0;
        height = 0;
        var parts = value.Split(',');
        return parts.Length == 2
            && long.TryParse(parts[0].Trim(), out width)
            && long.TryParse(parts[1].Trim(), out height);
    }
}
