using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class ResolveArchive
{
    private const int MiB = 1024 * 1024;

    public static ProjectFacts? Read(Stream source, bool isTemplate, CancellationToken token)
    {
        Span<byte> signature = stackalloc byte[4];
        if (source.ReadAtLeast(signature, signature.Length, false) != signature.Length ||
            !signature.SequenceEqual("PK\x03\x04"u8))
            return null;

        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var projectEntry = archive.GetEntry("project.xml");
        if (projectEntry is null && !isTemplate)
            return null;

        long budget = 96 * MiB;
        var facts = new ProjectFacts("Resolve", isTemplate ? "drt" : "drp", "DaVinci Resolve")
        {
            IsTemplate = isTemplate
        };

        var project = projectEntry is null ? null : ReadEntry(projectEntry, 32 * MiB);
        if (projectEntry is not null && project?.Name != "SM_Project")
            return null;
        facts.ApplicationVersion = project?.Annotation<string>() ?? (string?)project?.Element("ResolveVersion") ??
            (string?)project?.Element("Version") ?? (string?)project?.Attribute("Version");
        facts.ProjectTitle = (string?)project?.Element("ProjectName") ?? (string?)project?.Element("Name");

        var timelineIds = new HashSet<string>(StringComparer.Ordinal);
        var timelineCount = 0;
        var sequenceCount = 0;
        var sequenceEntries = 0;
        var mediaPoolEntries = 0;
        if (project is not null)
            AddTimelines(project);

        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.AsSpan();
            if (IsSequence(name))
            {
                if (++sequenceEntries > 256)
                {
                    facts.Truncated = true;
                    continue;
                }
                var sequence = ReadEntry(entry, 8 * MiB);
                if (sequence?.Name != "Sm2SequenceContainer")
                {
                    facts.Truncated = true;
                    continue;
                }
                sequenceCount++;
                var video = sequence.Element("VideoTrackVec")?.Descendants("Sm2TiTrack").Count() ?? 0;
                var audio = sequence.Element("AudioTrackVec")?.Descendants("Sm2TiTrack").Count() ?? 0;
                facts.VideoTrackCount = (facts.VideoTrackCount ?? 0) + video;
                facts.AudioTrackCount = (facts.AudioTrackCount ?? 0) + audio;
                var clips = sequence.Descendants("Sm2TiTrack").SelectMany(track =>
                    track.Element("Items")?.Descendants() ?? []).Count(item => item.Name.LocalName is
                        "Sm2TiVideoClip" or "Sm2TiAudioClip" or "Sm2TiGenerator" or "Sm2TiTitle");
                facts.ClipCount = (facts.ClipCount ?? 0) + clips;
            }
            else if (name.SequenceEqual("MpFolder.xml") ||
                (name.StartsWith("MediaPool/", StringComparison.Ordinal) &&
                 name.EndsWith("/MpFolder.xml", StringComparison.Ordinal)))
            {
                if (++mediaPoolEntries > 128)
                {
                    facts.Truncated = true;
                    continue;
                }
                if (ReadEntry(entry, 8 * MiB) is { } folder)
                {
                    facts.ApplicationVersion ??= folder.Annotation<string>();
                    AddTimelines(folder);
                }
            }
        }

        if (project is null && (sequenceCount == 0 || timelineCount == 0))
            return null;

        facts.TimelineCount = timelineCount > 0 ? timelineCount : sequenceCount;
        return facts;

        XElement? ReadEntry(ZipArchiveEntry entry, long maxLength)
        {
            var limit = Math.Min(budget, maxLength);
            if (entry.Length > limit || limit == 0)
            {
                facts.Truncated = true;
                return null;
            }

            using var unpacked = entry.Open();
            using var bounded = new LimitedProjectStream(unpacked, limit);
            try
            {
                return ProjectXml.Read(bounded, limit, token, resolveNames: true);
            }
            catch (Exception exception) when (exception is XmlException or IOException or InvalidDataException)
            {
                facts.Truncated = true;
                return null;
            }
            finally
            {
                budget = Math.Max(0, budget - bounded.Position);
            }
        }

        void AddTimelines(XElement document)
        {
            foreach (var timeline in document.Descendants("Sm2Timeline"))
            {
                token.ThrowIfCancellationRequested();
                if ((string?)timeline.Attribute("DbId") is { } id && !timelineIds.Add(id))
                    continue;
                timelineCount++;
                facts.AddTimelineName((string?)timeline.Element("Name"));
                var sequence = timeline.Element("Sequence")?.Element("Sm2Sequence");
                facts.FrameRate ??= ReadNumber((string?)timeline.Element("FrameRate") ??
                    (string?)sequence?.Element("FrameRate"), offset: 0);
                var extents = (string?)sequence?.Element("MediaExtents") ?? (string?)timeline.Element("MediaExtents");
                facts.Duration ??= ReadNumber(extents, offset: 16);
            }
        }
    }

    private static bool IsSequence(ReadOnlySpan<char> name) =>
        name.StartsWith("SeqContainer/", StringComparison.Ordinal) &&
        name.EndsWith(".xml", StringComparison.Ordinal) &&
        Guid.TryParse(name[13..^4], out _);

    private static double? ReadNumber(string? text, int offset)
    {
        if (text is null)
            return null;

        Span<byte> bytes = stackalloc byte[8];
        if (text.Length >= offset + 16 &&
            Convert.FromHexString(text.AsSpan(offset, 16), bytes, out _, out _) == System.Buffers.OperationStatus.Done)
        {
            var number = BinaryPrimitives.ReadDoubleLittleEndian(bytes);
            return double.IsFinite(number) && number >= 0 ? number : null;
        }
        return offset == 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) &&
            double.IsFinite(rate) && rate > 0 ? rate : null;
    }
}
