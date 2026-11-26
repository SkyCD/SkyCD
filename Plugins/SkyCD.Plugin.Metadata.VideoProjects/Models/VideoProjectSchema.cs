using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

internal static class VideoProjectSchema
{
    public static MediaMetadataSchema Metadata { get; } = new("skycd.videoprojects.metadata", 1,
    [
        new("Kind", MediaMetadataType.Text),
        new("Format", MediaMetadataType.Text),
        new("Application", MediaMetadataType.Text),
        new("ApplicationVersion", MediaMetadataType.Text),
        new("ProjectTitle", MediaMetadataType.Text),
        new("TimelineCount", MediaMetadataType.Integer),
        new("TimelineNames", MediaMetadataType.Text),
        new("TimelineTrackCounts", MediaMetadataType.Text),
        new("TrackCount", MediaMetadataType.Integer),
        new("VideoTrackCount", MediaMetadataType.Integer),
        new("AudioTrackCount", MediaMetadataType.Integer),
        new("ClipCount", MediaMetadataType.Integer),
        new("ItemCount", MediaMetadataType.Integer),
        new("MediaFileCount", MediaMetadataType.Integer),
        new("FolderCount", MediaMetadataType.Integer),
        new("FrameRate", MediaMetadataType.Number, "fps"),
        new("Duration", MediaMetadataType.Number, "s"),
        new("Resolution", MediaMetadataType.Text),
        new("IsTemplate", MediaMetadataType.Boolean),
        new("Truncated", MediaMetadataType.Boolean)
    ]);
}
