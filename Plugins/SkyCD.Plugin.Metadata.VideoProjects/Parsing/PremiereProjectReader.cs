using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Streams a Premiere Pro project (plain or gzip-wrapped XML) and keeps only the facts a catalog needs.
/// Premiere stores every timeline position as an integer tick; 254 016 000 000 ticks make one second.
/// </summary>
internal static class PremiereProjectReader
{
    private const long TicksPerSecond = 254_016_000_000;
    private const int MaxMediaPaths = 100_000;

    public static VideoProjectFacts Read(
        XmlReader reader, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        string? projectVersion = null;
        long sequenceCount = 0;
        long trackCount = 0;
        long clipCount = 0;
        long itemCount = 0;
        long latestEnd = 0;
        long? ticksPerFrame = null;
        string? resolution = null;
        var timelineNames = new List<string>();
        var mediaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > ProjectXml.MaxDepth)
            {
                throw new VideoProjectLimitExceededException("XML depth", ProjectXml.MaxDepth);
            }

            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "Project":
                    // The first Project element is an ObjectRef pointer; the real one carries ObjectID.
                    if (projectVersion is null && reader.GetAttribute("ObjectID") is not null)
                    {
                        projectVersion = ProjectText.Clean(reader.GetAttribute("Version"), 32);
                    }

                    break;

                case "Sequence":
                    sequenceCount++;
                    if (timelineNames.Count < options.MaxTimelineNames)
                    {
                        var name = ProjectText.Clean(
                            ProjectXml.ReadFirstChildText(reader, "Name", options.MaxNameCharacters),
                            options.MaxNameCharacters);
                        if (name is not null)
                        {
                            timelineNames.Add(name);
                        }
                    }

                    break;

                case "VideoTrackGroup":
                    trackCount += ReadTrackGroup(reader, out var groupTicks, out var groupResolution);
                    ticksPerFrame ??= groupTicks;
                    resolution ??= groupResolution;
                    break;

                case "AudioTrackGroup":
                    trackCount += ReadTrackGroup(reader, out _, out _);
                    break;

                case "VideoClipTrackItem" when reader.GetAttribute("ObjectID") is not null:
                case "AudioClipTrackItem" when reader.GetAttribute("ObjectID") is not null:
                    clipCount++;
                    latestEnd = Math.Max(latestEnd, ReadTrackItemEnd(reader));
                    break;

                case "ClipProjectItem" when reader.GetAttribute("ObjectID") is not null:
                case "BinProjectItem" when reader.GetAttribute("ObjectID") is not null:
                    itemCount++;
                    break;

                case "Media":
                    if (mediaPaths.Count < MaxMediaPaths)
                    {
                        var path = ProjectXml.ReadFirstChildText(reader, "FilePath", 512);
                        if (IsFileSystemPath(path))
                        {
                            mediaPaths.Add(path!);
                        }
                    }

                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new VideoProjectFacts(
            VideoProjectIdentity.KindPremiere,
            VideoProjectIdentity.FormatPrproj,
            VideoProjectIdentity.ApplicationPremiere)
        {
            ApplicationVersion = projectVersion,
            ProjectTitle = timelineNames.Count > 0 ? timelineNames[0] : null,
            TimelineCount = sequenceCount,
            TimelineNames = timelineNames,
            TrackCount = trackCount,
            ClipCount = clipCount,
            ItemCount = itemCount,
            MediaFileCount = mediaPaths.Count,
            FrameRate = FrameRateFromTicks(ticksPerFrame),
            DurationSeconds = latestEnd > 0 ? latestEnd / (double)TicksPerSecond : null,
            Resolution = resolution
        };
    }

    private static long ReadTrackGroup(XmlReader reader, out long? ticksPerFrame, out string? resolution)
    {
        long tracks = 0;
        ticksPerFrame = null;
        resolution = null;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.LocalName)
            {
                case "Track":
                    tracks++;
                    break;

                case "FrameRate" when ticksPerFrame is null:
                    var text = ProjectXml.ReadElementText(subtree, 32);
                    if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var ticks) && ticks > 0)
                    {
                        ticksPerFrame = ticks;
                    }

                    break;

                case "FrameRect" when resolution is null:
                    resolution = ParseResolution(ProjectXml.ReadElementText(subtree, 64));
                    break;
            }
        }

        return tracks;
    }

    private static long ReadTrackItemEnd(XmlReader reader)
    {
        long end = 0;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element ||
                (subtree.LocalName != "Start" && subtree.LocalName != "End"))
            {
                continue;
            }

            var text = ProjectXml.ReadElementText(subtree, 32);
            if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) &&
                ticks > end && ticks < long.MaxValue / 2)
            {
                end = ticks;
            }
        }

        return end;
    }

    private static double? FrameRateFromTicks(long? ticksPerFrame)
    {
        if (ticksPerFrame is not > 0)
        {
            return null;
        }

        var frameRate = TicksPerSecond / (double)ticksPerFrame.Value;
        return frameRate is >= 1 and <= 240 ? Math.Round(frameRate, 3) : null;
    }

    /// <summary>Parses Premiere's <c>FrameRect</c> (<c>x,y,width,height</c> or <c>width,height</c>).</summary>
    private static string? ParseResolution(string rect)
    {
        var parts = rect.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 4)
        {
            return null;
        }

        var widthText = parts[^2];
        var heightText = parts[^1];
        if (!int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width is <= 0 or > 32768 || height is <= 0 or > 32768)
        {
            return null;
        }

        return $"{width}x{height}";
    }

    /// <summary>
    /// Premiere's built-in generators (Black Video, Color Matte, titles) store an internal numeric id rather
    /// than a filesystem path, so only paths that look like a path count as referenced media.
    /// </summary>
    private static bool IsFileSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path!.Trim().Length < 3)
        {
            return false;
        }

        var trimmed = path.Trim();
        return trimmed.Contains('\\', StringComparison.Ordinal) ||
               trimmed.Contains('/', StringComparison.Ordinal) ||
               trimmed.Contains(':', StringComparison.Ordinal);
    }
}
