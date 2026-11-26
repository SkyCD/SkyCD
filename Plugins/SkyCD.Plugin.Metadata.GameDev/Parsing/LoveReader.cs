using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using SkyCD.Plugin.Metadata.GameDev.Exceptions;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads a LÖVE <c>.love</c> archive from the ZIP directory: entry count, the presence of <c>main.lua</c> and
/// <c>conf.lua</c>, and a literal version from <c>conf.lua</c> when one is present.
/// </summary>
internal static partial class LoveReader
{
    internal static GameDevFacts? Read(IMediaSource source, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = GameDevSource.OpenZip(source, cancellationToken);
            var facts = new GameDevFacts
            {
                Family = "LÖVE",
                Format = "LÖVE game",
                Application = "LÖVE"
            };

            var entries = 0;
            var hasMain = false;
            ZipArchiveEntry? conf = null;
            foreach (var entry in archive.Entries)
            {
                if (++entries > GameDevLimits.MaxArchiveEntries)
                {
                    facts.ScanIncomplete = true;
                    break;
                }

                if (string.Equals(entry.FullName, "main.lua", StringComparison.OrdinalIgnoreCase))
                {
                    hasMain = true;
                }
                else if (string.Equals(entry.FullName, "conf.lua", StringComparison.OrdinalIgnoreCase))
                {
                    conf = entry;
                }
            }

            facts.FileCount = entries;
            facts.AssetCount = entries;
            facts.ScriptCount = hasMain ? 1 : 0;
            facts.ResourceCount = conf is null ? 0 : 1;

            if (conf is not null)
            {
                var text = GameDevSource.DecodeText(GameDevSource.ReadEntry(conf, 64 * 1024));
                var match = VersionLine().Match(text);
                if (match.Success)
                {
                    facts.EngineVersion = match.Groups["Version"].Value;
                }
            }

            facts.AddPreviewRow("Format: LÖVE game");
            facts.AddPreviewRow("main.lua: " + hasMain);
            return facts;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or GameDevLimitExceededException or NotSupportedException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"t\.version\s*=\s*""(?<Version>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();
}
