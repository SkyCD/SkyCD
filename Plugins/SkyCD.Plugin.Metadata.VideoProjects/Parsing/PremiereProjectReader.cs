using System.Xml.Linq;
using ICSharpCode.SharpZipLib.GZip;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class PremiereProjectReader
{
    public static ProjectFacts? Read(Stream source, CancellationToken token)
    {
        var gzip = source.ReadByte() == 0x1f && source.ReadByte() == 0x8b;
        source.Position = 0;
        using var compressed = gzip ? new GZipInputStream(source) { IsStreamOwner = false } : null;
        var root = ProjectXml.Read(compressed ?? source, 64 * 1024 * 1024, token);
        if (root?.Name != "PremiereData" || (string?)root.Attribute("Version") is null)
            return null;

        var objects = root.Elements().Where(element =>
            element.Attribute("ObjectRef") is null && element.Attribute("ObjectURef") is null).ToArray();
        var byId = Index("ObjectID");
        var byUid = Index("ObjectUID");
        var sequences = objects.Where(element => element.Name == "Sequence").ToArray();
        var project = root.Element("Project");
        var facts = new ProjectFacts("Premiere", "prproj", "Adobe Premiere Pro")
        {
            ApplicationVersion = (string?)project?.Attribute("Version") ?? (string?)root.Attribute("Version"),
            ProjectTitle = (string?)project?.Element("Name"),
            TimelineCount = sequences.Length,
            ItemCount = objects.Count(element => element.Name.LocalName is "ClipProjectItem" or "BinProjectItem"),
            MediaFileCount = objects.Count(element => element.Name == "Media" &&
                element.Elements().Any(path => path.Name.LocalName is "FilePath" or "ActualMediaFilePath" &&
                    !string.IsNullOrWhiteSpace(path.Value)))
        };

        foreach (var sequence in sequences)
        {
            token.ThrowIfCancellationRequested();
            var name = (string?)sequence.Element("Name") ?? (string?)sequence.Element("Node")?.Element("Properties")?.Element("Name");
            facts.AddTimelineName(name);
            var tracks = sequence.Descendants().Select(Resolve)
                .Where(element => element.Name.LocalName is "VideoTrackGroup" or "AudioTrackGroup")
                .Distinct()
                .SelectMany(group => group.Descendants("Tracks").SelectMany(list => list.Elements()))
                .Select(Resolve)
                .Concat(sequence.Descendants().Where(element => element.Name.LocalName is "VideoTrack" or "AudioTrack").Select(Resolve))
                .Distinct().ToArray();
            var video = tracks.Count(element => element.Name.LocalName is "VideoClipTrack" or "VideoTrack");
            var audio = tracks.Count(element => element.Name.LocalName is "AudioClipTrack" or "AudioTrack");
            facts.VideoTrackCount = (facts.VideoTrackCount ?? 0) + video;
            facts.AudioTrackCount = (facts.AudioTrackCount ?? 0) + audio;
            if (facts.TimelineTrackCounts.Count < 32)
                facts.TimelineTrackCounts.Add(FormattableString.Invariant($"{video} video, {audio} audio"));
        }
        return facts;

        Dictionary<string, XElement> Index(string attribute)
        {
            var index = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var element in objects)
            {
                if ((string?)element.Attribute(attribute) is { } id)
                    index.TryAdd(id, element);
            }
            return index;
        }

        XElement Resolve(XElement element)
        {
            if ((string?)element.Attribute("ObjectRef") is { } reference && byId.TryGetValue(reference, out var target))
                return target;
            if ((string?)element.Attribute("ObjectURef") is { } uid && byUid.TryGetValue(uid, out target))
                return target;
            return element;
        }
    }
}
