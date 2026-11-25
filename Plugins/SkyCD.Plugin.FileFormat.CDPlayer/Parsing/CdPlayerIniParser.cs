using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SkyCD.Plugin.EncodingProfiles;
using SkyCD.Plugin.FileFormat.CDPlayer.Exceptions;
using SkyCD.Plugin.FileFormat.CDPlayer.Models;

namespace SkyCD.Plugin.FileFormat.CDPlayer.Parsing;

/// <summary>
/// Parser and converter for classic Windows CDPlayer.ini files.
/// </summary>
public static class CdPlayerIniParser
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10 MiB safety limit (classic profile limit was 64 KiB)
    public const int MaxLineLength = 4096;
    public const int MaxValueLength = 2048;

    private static readonly HashSet<string> KnownGeneralKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "artist",
        "title",
        "numtracks",
        "order",
        "numplay",
        "entrytype"
    };

    /// <summary>
    /// Checks whether stream content looks like a valid CDPlayer.ini file without modifying position.
    /// </summary>
    public static bool CanHandle(Stream stream, Encoding? preferredEncoding = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var detectedEncoding = preferredEncoding ?? DetectEncoding(stream);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new StreamReader(stream, detectedEncoding, detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true);

            var sections = ReadSections(reader, maxLinesToInspect: 500);
            return sections.Any(IsPlausibleCdSection);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    /// <summary>
    /// Parses a CDPlayer.ini stream and builds a structured CdPlayerIniReportDocument.
    /// </summary>
    public static CdPlayerIniReportDocument Parse(
        Stream stream,
        long maxSizeBytes = DefaultMaxFileSizeBytes,
        Encoding? preferredEncoding = null)
    {
        if (stream.CanSeek && stream.Length > maxSizeBytes)
        {
            throw new CdPlayerFileSizeLimitExceededException(maxSizeBytes);
        }

        var encoding = preferredEncoding ?? DetectEncoding(stream);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        var sections = ReadSections(reader, maxLinesToInspect: null, maxSizeBytes: maxSizeBytes);

        var cdSections = sections.Where(IsPlausibleCdSection).ToList();
        if (cdSections.Count == 0)
        {
            throw new CdPlayerInvalidFormatException("No valid Audio CD sections found in the INI file.");
        }

        var warnings = new List<string>();
        var diagnostics = new List<string>();
        var discNames = new List<string>();
        var totalTracks = 0;

        var rows = new List<Dictionary<string, object?>>();

        foreach (var section in cdSections)
        {
            var sectionId = section.SectionName;
            var keys = section.Entries;

            var artist = GetValue(keys, "artist");
            var title = GetValue(keys, "title");
            var numTracksRaw = GetValue(keys, "numtracks");
            var order = GetValue(keys, "order");
            var numPlay = GetValue(keys, "numplay");
            var entryType = GetValue(keys, "entrytype");

            var rootName = !string.IsNullOrWhiteSpace(title) ? title.Trim() : sectionId;
            discNames.Add(rootName);

            var rootNodeId = $"cd:{sectionId.ToLowerInvariant()}";

            var rootProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DiscId"] = sectionId,
                ["SectionId"] = sectionId
            };

            if (!string.IsNullOrWhiteSpace(artist))
            {
                rootProperties["Artist"] = artist.Trim();
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                rootProperties["Title"] = title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(entryType))
            {
                rootProperties["EntryType"] = entryType.Trim();
            }

            if (!string.IsNullOrWhiteSpace(order))
            {
                rootProperties["Order"] = order.Trim();
            }

            if (!string.IsNullOrWhiteSpace(numPlay))
            {
                rootProperties["NumPlay"] = numPlay.Trim();
            }

            // Include any unknown keys as properties
            foreach (var (k, v) in keys)
            {
                if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                if (!KnownGeneralKeys.Contains(k))
                {
                    rootProperties[k] = TruncateValue(v);
                }
            }

            var declaredTrackCount = -1;
            if (int.TryParse(numTracksRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dtc) && dtc >= 0)
            {
                declaredTrackCount = dtc;
                rootProperties["NumTracks"] = declaredTrackCount.ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrWhiteSpace(numTracksRaw))
            {
                warnings.Add($"Disc [{sectionId}]: Invalid 'numtracks' value '{numTracksRaw}'.");
            }

            // Collect numeric track keys
            var numericTrackEntries = new Dictionary<int, string>();
            foreach (var (k, v) in keys)
            {
                if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackIndex) && trackIndex >= 0)
                {
                    numericTrackEntries[trackIndex] = v;
                }
            }

            var maxIndex = numericTrackEntries.Count > 0 ? numericTrackEntries.Keys.Max() : -1;
            var trackIndices = new SortedSet<int>(numericTrackEntries.Keys);

            // If declaredTrackCount is specified and > maxIndex + 1, we fill missing indices up to declaredTrackCount
            var effectiveTrackCount = declaredTrackCount >= 0
                ? Math.Max(declaredTrackCount, maxIndex + 1)
                : maxIndex + 1;

            if (declaredTrackCount >= 0 && numericTrackEntries.Count != declaredTrackCount)
            {
                warnings.Add($"Disc [{sectionId}]: Declared track count is {declaredTrackCount}, but found {numericTrackEntries.Count} track definitions.");
            }

            var rootRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = rootNodeId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = rootName,
                ["sizeBytes"] = "0",
                ["properties"] = rootProperties
            };
            rows.Add(rootRow);

            var discTrackCount = 0;
            for (var i = 0; i < effectiveTrackCount; i++)
            {
                discTrackCount++;
                totalTracks++;

                var trackNumber = i + 1;
                string trackTitle;

                if (numericTrackEntries.TryGetValue(i, out var definedTitle) && !string.IsNullOrWhiteSpace(definedTitle))
                {
                    trackTitle = TruncateValue(definedTitle.Trim());
                }
                else
                {
                    trackTitle = FormattableString.Invariant($"Track {trackNumber:D2}");
                }

                var trackNodeId = $"{rootNodeId}/track-{trackNumber:D2}";
                var trackProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TrackNumber"] = trackNumber.ToString(CultureInfo.InvariantCulture),
                    ["TrackIndex"] = i.ToString(CultureInfo.InvariantCulture),
                    ["DiscId"] = sectionId
                };

                if (!string.IsNullOrWhiteSpace(artist))
                {
                    trackProperties["Artist"] = artist.Trim();
                }

                var trackRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = trackNodeId,
                    ["parentId"] = rootNodeId,
                    ["kind"] = "file",
                    ["name"] = trackTitle,
                    ["sizeBytes"] = "0",
                    ["properties"] = trackProperties
                };
                rows.Add(trackRow);
            }

            diagnostics.Add($"Disc [{sectionId}]: Imported '{rootName}' with {discTrackCount} tracks.");
        }

        var summary = new CdPlayerIniImportSummary(
            TotalDiscsRead: cdSections.Count,
            TotalTracksRead: totalTracks,
            Discs: discNames,
            Warnings: warnings,
            Diagnostics: diagnostics,
            DetectedEncoding: encoding.WebName);

        return new CdPlayerIniReportDocument(rows, summary);
    }

    /// <summary>
    /// Determines whether an INI section looks like a plausible CD section.
    /// A section is plausible if its name is not a known non-CD section and it contains
    /// CD-specific keys (artist, title, numtracks, or 0= track entries).
    /// </summary>
    public static bool IsPlausibleCdSection(IniSection section)
    {
        var name = section.SectionName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Section name shouldn't be a generic non-CD section like [Options], [Settings], [CDPlayer]
        if (name.Equals("Options", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CDPlayer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var entries = section.Entries;

        var hasArtist = entries.ContainsKey("artist");
        var hasTitle = entries.ContainsKey("title");
        var hasNumTracks = entries.ContainsKey("numtracks");
        var hasTrack0 = entries.ContainsKey("0");
        var hasEntryType = entries.ContainsKey("entrytype");
        var hasOrder = entries.ContainsKey("order");

        // Must have at least two CD indicators or artist/title + numtracks/track0
        var score = 0;
        if (hasArtist) score++;
        if (hasTitle) score++;
        if (hasNumTracks) score += 2;
        if (hasTrack0) score += 2;
        if (hasEntryType) score++;
        if (hasOrder) score++;

        return score >= 2;
    }

    /// <summary>
    /// Detects whether the stream is UTF-8 (with BOM or valid UTF-8 without replacement) or ANSI (Windows-1252 / Default).
    /// Throws CdPlayerEncodingException if strict decoding fails.
    /// </summary>
    public static Encoding DetectEncoding(Stream stream)
    {
        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var headerBytes = new byte[Math.Min(stream.Length - (stream.CanSeek ? stream.Position : 0), 65536)];
            var readCount = stream.Read(headerBytes, 0, headerBytes.Length);

            if (readCount >= 3 && headerBytes[0] == 0xEF && headerBytes[1] == 0xBB && headerBytes[2] == 0xBF)
            {
                return Encoding.UTF8;
            }

            // Test if valid UTF-8
            if (IsValidUtf8(headerBytes.AsSpan(0, readCount), out var hasNonAscii))
            {
                if (hasNonAscii)
                {
                    return new UTF8Encoding(false, throwOnInvalidBytes: true);
                }
            }

            // Fall back to Windows-1252 (ANSI) or Latin1
            try
            {
                return LegacyEncoding.Windows1252;
            }
            catch
            {
                return Encoding.Latin1;
            }
        }
        catch (Exception ex)
        {
            throw new CdPlayerEncodingException("AutoDetect", ex.Message, ex);
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, out bool hasNonAscii)
    {
        hasNonAscii = false;
        var i = 0;
        while (i < bytes.Length)
        {
            var b0 = bytes[i++];
            if (b0 <= 0x7F)
            {
                continue;
            }

            hasNonAscii = true;
            int remaining;
            if ((b0 & 0xE0) == 0xC0)
            {
                if (b0 < 0xC2) return false; // Overlong
                remaining = 1;
            }
            else if ((b0 & 0xF0) == 0xE0)
            {
                remaining = 2;
            }
            else if ((b0 & 0xF8) == 0xF0)
            {
                if (b0 > 0xF4) return false; // Out of Unicode range
                remaining = 3;
            }
            else
            {
                return false;
            }

            if (i + remaining > bytes.Length)
            {
                // Incomplete multi-byte sequence at buffer end
                break;
            }

            for (var j = 0; j < remaining; j++)
            {
                var bx = bytes[i++];
                if ((bx & 0xC0) != 0x80)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static List<IniSection> ReadSections(TextReader reader, int? maxLinesToInspect = null, long? maxSizeBytes = null)
    {
        var sections = new List<IniSection>();
        IniSection? currentSection = null;
        var linesRead = 0;
        long bytesEstimated = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            linesRead++;
            bytesEstimated += line.Length + 2;

            if (maxSizeBytes.HasValue && bytesEstimated > maxSizeBytes.Value)
            {
                throw new CdPlayerFileSizeLimitExceededException(maxSizeBytes.Value);
            }

            if (maxLinesToInspect.HasValue && linesRead > maxLinesToInspect.Value)
            {
                break;
            }

            if (line.Length > MaxLineLength)
            {
                line = line[..MaxLineLength];
            }

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                var sectionName = trimmed[1..^1].Trim();
                // Check if section already exists (duplicate section handling: merge into existing)
                var existing = sections.FirstOrDefault(s => s.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    currentSection = existing;
                }
                else
                {
                    currentSection = new IniSection(sectionName);
                    sections.Add(currentSection);
                }
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex > 0)
            {
                var key = trimmed[..equalsIndex].Trim();
                var value = trimmed[(equalsIndex + 1)..].Trim();
                currentSection.Entries[key] = value; // Later duplicate keys in same section overwrite earlier keys
            }
        }

        return sections;
    }

    private static string? GetValue(Dictionary<string, string> dict, string key)
    {
        return dict.TryGetValue(key, out var val) ? val : null;
    }

    private static string TruncateValue(string value)
    {
        if (value.Length > MaxValueLength)
        {
            return value[..MaxValueLength];
        }

        return value;
    }

    public sealed class IniSection
    {
        public string SectionName { get; }
        public Dictionary<string, string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IniSection(string sectionName)
        {
            SectionName = sectionName;
        }
    }
}
