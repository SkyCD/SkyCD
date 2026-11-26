using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

internal sealed class ProjectFacts(string kind, string format, string application)
{
    public string? ApplicationVersion { get; set; }
    public string? ProjectTitle { get; set; }
    public int? TimelineCount { get; set; }
    public List<string> TimelineNames { get; } = [];
    public List<string> TimelineTrackCounts { get; } = [];
    public int? VideoTrackCount { get; set; }
    public int? AudioTrackCount { get; set; }
    public int? ClipCount { get; set; }
    public int? ItemCount { get; set; }
    public int? MediaFileCount { get; set; }
    public int? FolderCount { get; set; }
    public double? FrameRate { get; set; }
    public double? Duration { get; set; }
    public string? Resolution { get; set; }
    public bool? IsTemplate { get; set; }
    public bool Truncated { get; set; }

    public void AddTimelineName(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && TimelineNames.Count < 32)
            TimelineNames.Add(value[..Math.Min(value.Length, 256)]);
    }

    public MediaExtractionResult ToResult()
    {
        var values = new List<MediaMetadataValue>();
        Text("Kind", kind);
        Text("Format", format);
        Text("Application", application);
        Text("ApplicationVersion", ApplicationVersion);
        Text("ProjectTitle", ProjectTitle);
        Integer("TimelineCount", TimelineCount);
        Text("TimelineNames", string.Join("\n", TimelineNames));
        Text("TimelineTrackCounts", string.Join("\n", TimelineTrackCounts));
        Integer("VideoTrackCount", VideoTrackCount);
        Integer("AudioTrackCount", AudioTrackCount);
        if (VideoTrackCount.HasValue || AudioTrackCount.HasValue)
            Integer("TrackCount", (VideoTrackCount ?? 0) + (AudioTrackCount ?? 0));
        Integer("ClipCount", ClipCount);
        Integer("ItemCount", ItemCount);
        Integer("MediaFileCount", MediaFileCount);
        Integer("FolderCount", FolderCount);
        Number("FrameRate", FrameRate);
        Number("Duration", Duration);
        Text("Resolution", Resolution);
        if (IsTemplate is { } template)
            values.Add(new("IsTemplate", MediaMetadataType.Boolean, Boolean: template));
        if (Truncated)
            values.Add(new("Truncated", MediaMetadataType.Boolean, Boolean: true));
        return new(values);

        void Text(string key, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                values.Add(new(key, MediaMetadataType.Text, Text: text[..Math.Min(text.Length, 8192)]));
        }

        void Integer(string key, int? number)
        {
            if (number is { } value)
                values.Add(new(key, MediaMetadataType.Integer, Integer: value));
        }

        void Number(string key, double? number)
        {
            if (number is { } value && double.IsFinite(value) && value >= 0)
                values.Add(new(key, MediaMetadataType.Number, Number: value));
        }
    }
}
