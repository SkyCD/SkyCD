using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Reads a DaVinci Resolve project (<c>.drp</c>) or template (<c>.drt</c>). The container is a ZIP whose
/// <c>project.xml</c> describes the media pool and whose <c>SeqContainer</c> entries hold one document per
/// timeline. Entries are located by their expected names, inflated under byte caps and never extracted to disk.
/// </summary>
internal static class ResolveProjectReader
{
    private const string ProjectEntry = "project.xml";
    private const string MediaPoolEntry = "MediaPool/Master/MpFolder.xml";
    private const string SeqContainerPrefix = "SeqContainer/";
    private const int MaxApplicationVersionCharacters = 64;

    public static VideoProjectFacts? Read(
        IMediaSource source,
        string extension,
        VideoProjectPluginOptions options,
        CancellationToken cancellationToken)
    {
        using var stream = new MediaSourceStream(source, cancellationToken);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        ZipArchiveEntry? project = null;
        ZipArchiveEntry? mediaPool = null;
        var sequences = new List<ZipArchiveEntry>();
        var visited = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > options.MaxArchiveEntries)
            {
                throw new VideoProjectLimitExceededException("archive entry count", options.MaxArchiveEntries);
            }

            var name = entry.FullName.Replace('\\', '/');
            if (name.Equals(ProjectEntry, StringComparison.OrdinalIgnoreCase))
            {
                project ??= entry;
            }
            else if (name.Equals(MediaPoolEntry, StringComparison.OrdinalIgnoreCase))
            {
                mediaPool ??= entry;
            }
            else if (name.StartsWith(SeqContainerPrefix, StringComparison.OrdinalIgnoreCase) &&
                     name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                     name.Length > SeqContainerPrefix.Length + 4)
            {
                if (sequences.Count >= options.MaxSeqContainers)
                {
                    throw new VideoProjectLimitExceededException("timeline document count", options.MaxSeqContainers);
                }

                sequences.Add(entry);
            }
        }

        if (project is null)
        {
            return null;
        }

        long inflated = 0;
        var projectDocument = ReadEntry(project, options, ref inflated, cancellationToken);
        var context = ReadProject(projectDocument, options, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var timelineCount = context.TimelineCount;
        long? trackCount = null;
        long? clipCount = null;
        double? frameRate = null;
        double? durationSeconds = null;

        if (sequences.Count > 0)
        {
            var sequence = ReadSequence(
                ReadEntry(sequences[0], options, ref inflated, cancellationToken), options, cancellationToken);
            timelineCount ??= sequences.Count;
            trackCount = sequence.TrackCount;
            clipCount = sequence.ClipCount;
            frameRate = sequence.FrameRate;
            durationSeconds = sequence.DurationSeconds;
        }

        long? mediaFileCount = null;
        if (mediaPool is not null)
        {
            var pool = ReadMediaPool(
                ReadEntry(mediaPool, options, ref inflated, cancellationToken), options, cancellationToken);
            timelineCount ??= pool.TimelineCount;
            mediaFileCount = pool.MediaClipCount;
            if (context.TimelineNames.Count == 0)
            {
                context.TimelineNames.AddRange(pool.TimelineNames);
            }
        }

        var isTemplate = extension.Equals(".drt", StringComparison.OrdinalIgnoreCase);

        return new VideoProjectFacts(
            VideoProjectIdentity.KindResolve,
            isTemplate ? VideoProjectIdentity.FormatDrt : VideoProjectIdentity.FormatDrp,
            VideoProjectIdentity.ApplicationResolve)
        {
            ApplicationVersion = context.ApplicationVersion,
            ProjectTitle = context.TimelineNames.Count > 0 ? context.TimelineNames[0] : null,
            TimelineCount = timelineCount,
            TimelineNames = context.TimelineNames,
            TrackCount = trackCount,
            ClipCount = clipCount,
            MediaFileCount = mediaFileCount,
            FrameRate = frameRate,
            DurationSeconds = durationSeconds,
            IsTemplate = isTemplate ? true : null
        };
    }

    private static ProjectContext? ReadProject(
        byte[] xml, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        using var reader = ProjectXml.Create(new MemoryStream(xml, writable: false), options.MaxProjectXmlBytes);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "SM_Project")
        {
            return null;
        }

        var version = ReadVersionAttribute(reader) ?? FindDeclaredVersion(xml);

        long timelineCount = 0;
        var timelineNames = new List<string>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > ProjectXml.MaxDepth)
            {
                throw new VideoProjectLimitExceededException("XML depth", ProjectXml.MaxDepth);
            }

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Sm2MpTimelineClip")
            {
                continue;
            }

            timelineCount++;
            if (timelineNames.Count < options.MaxTimelineNames)
            {
                var name = ProjectText.Clean(
                    ProjectXml.ReadFirstChildText(reader, "Name", options.MaxNameCharacters),
                    options.MaxNameCharacters);
                if (name is not null && !timelineNames.Contains(name))
                {
                    timelineNames.Add(name);
                }
            }
        }

        return new ProjectContext(version, timelineCount > 0 ? timelineCount : null, timelineNames);
    }

    private static string? ReadVersionAttribute(XmlReader reader)
    {
        foreach (var attribute in new[] { "DbAppVer", "AppVersion", "Version" })
        {
            var value = ProjectText.Clean(reader.GetAttribute(attribute), MaxApplicationVersionCharacters);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Some Resolve builds only state the application version in the comment above the document. The comment is
    /// not part of the parsed tree, so the leading bytes are scanned for the literal <c>DbAppVer="..."</c> form.
    /// </summary>
    private static string? FindDeclaredVersion(byte[] xml)
    {
        var prefix = System.Text.Encoding.UTF8.GetString(xml, 0, Math.Min(xml.Length, 8192));
        const string marker = "DbAppVer=\"";
        var start = prefix.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = prefix.IndexOf('"', start);
        return end <= start
            ? null
            : ProjectText.Clean(prefix[start..end], MaxApplicationVersionCharacters);
    }

    private static SequenceFacts ReadSequence(
        byte[] xml, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        using var reader = ProjectXml.Create(new MemoryStream(xml, writable: false), options.MaxProjectXmlBytes);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "Sm2SequenceContainer")
        {
            return new SequenceFacts();
        }

        long trackCount = 0;
        long clipCount = 0;
        long latestEnd = 0;
        double? frameRate = null;
        var trackVectorDepth = -1;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > ProjectXml.MaxDepth)
            {
                throw new VideoProjectLimitExceededException("XML depth", ProjectXml.MaxDepth);
            }

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == trackVectorDepth)
            {
                trackVectorDepth = -1;
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var name = reader.LocalName;
            if (name is "VideoTrackVec" or "AudioTrackVec")
            {
                trackVectorDepth = reader.Depth;
                continue;
            }

            if (trackVectorDepth >= 0 && name == "Sm2TiTrack")
            {
                trackCount++;
                continue;
            }

            if (frameRate is null && name == "MediaFrameRate")
            {
                frameRate = ReadFrameRate(ProjectXml.ReadElementText(reader, 32));
                continue;
            }

            if (name.StartsWith("Sm2Ti", StringComparison.Ordinal) &&
                name.EndsWith("Clip", StringComparison.Ordinal))
            {
                clipCount++;
                latestEnd = Math.Max(latestEnd, ReadClipEnd(reader));
            }
        }

        var durationSeconds = frameRate is > 0 && latestEnd > 0
            ? (double?)Math.Round(latestEnd / frameRate.Value, 3)
            : null;

        return new SequenceFacts
        {
            TrackCount = trackCount,
            ClipCount = clipCount,
            FrameRate = frameRate,
            DurationSeconds = durationSeconds
        };
    }

    private static double? ReadFrameRate(string text) =>
        HexPrefix.Decode(text, 8) is { Length: 8 } bytes
            ? ReadRate(BinaryPrimitives.ReadDoubleLittleEndian(bytes))
            : null;

    private static double? ReadRate(double rate) => rate is >= 1 and <= 240 ? Math.Round(rate, 3) : null;

    private static long ReadClipEnd(XmlReader reader)
    {
        long start = 0;
        long duration = 0;
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.LocalName)
            {
                case "Start":
                    start = ParseFrameCount(ProjectXml.ReadElementText(subtree, 32));
                    break;

                case "Duration":
                    duration = ParseFrameCount(ProjectXml.ReadElementText(subtree, 32));
                    break;
            }
        }

        var end = start + duration;
        return end > start ? end : 0;
    }

    private static long ParseFrameCount(string text) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames) &&
        frames is > 0 and < 100_000_000
            ? frames
            : 0;

    private static MediaPoolFacts ReadMediaPool(
        byte[] xml, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        using var reader = ProjectXml.Create(new MemoryStream(xml, writable: false), options.MaxProjectXmlBytes);
        if (reader.MoveToContent() != XmlNodeType.Element)
        {
            return new MediaPoolFacts();
        }

        long timelineCount = 0;
        long mediaClipCount = 0;
        var timelineNames = new List<string>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > ProjectXml.MaxDepth)
            {
                throw new VideoProjectLimitExceededException("XML depth", ProjectXml.MaxDepth);
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName == "Sm2MpTimelineClip")
            {
                timelineCount++;
                if (timelineNames.Count < options.MaxTimelineNames)
                {
                    var name = ProjectText.Clean(
                        ProjectXml.ReadFirstChildText(reader, "Name", options.MaxNameCharacters),
                        options.MaxNameCharacters);
                    if (name is not null && !timelineNames.Contains(name))
                    {
                        timelineNames.Add(name);
                    }
                }
            }
            else if (reader.LocalName.StartsWith("Sm2Mp", StringComparison.Ordinal) &&
                     reader.LocalName.EndsWith("Clip", StringComparison.Ordinal))
            {
                mediaClipCount++;
            }
        }

        return new MediaPoolFacts
        {
            TimelineCount = timelineCount > 0 ? timelineCount : null,
            TimelineNames = timelineNames,
            MediaClipCount = mediaClipCount
        };
    }

    private static byte[] ReadEntry(
        ZipArchiveEntry entry,
        VideoProjectPluginOptions options,
        ref long inflated,
        CancellationToken cancellationToken)
    {
        if (entry.Length > options.MaxArchiveEntryBytes)
        {
            throw new VideoProjectLimitExceededException("archive entry size", options.MaxArchiveEntryBytes);
        }

        var remaining = options.MaxArchiveTotalBytes - inflated;
        if (entry.Length > remaining)
        {
            throw new VideoProjectLimitExceededException(
                "total inflated archive size", options.MaxArchiveTotalBytes);
        }

        using var entryStream = entry.Open();
        using var bounded = new BoundedReadStream(entryStream, remaining, "total inflated archive size");
        var data = new MemoryStream((int)entry.Length);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = bounded.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inflated += read;
            data.Write(buffer, 0, read);
        }

        return data.ToArray();
    }

    private sealed record ProjectContext(string? ApplicationVersion, long? TimelineCount, List<string> TimelineNames);

    private sealed record MediaPoolFacts
    {
        public long? TimelineCount { get; init; }

        public List<string> TimelineNames { get; init; } = [];

        public long? MediaClipCount { get; init; }
    }

    private sealed record SequenceFacts
    {
        public long? TrackCount { get; init; }

        public long? ClipCount { get; init; }

        public double? FrameRate { get; init; }

        public double? DurationSeconds { get; init; }
    }
}
