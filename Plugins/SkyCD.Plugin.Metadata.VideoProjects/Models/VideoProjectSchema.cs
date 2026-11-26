using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

/// <summary>
/// Versioned schema for video editing project facts. Only fields the project actually carries are
/// published, so an absent value stays distinguishable from an empty one.
/// </summary>
public static class VideoProjectSchema
{
    public const string MetadataSchemaId = "skycd.videoprojects.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string Kind = "Kind";
    public const string Format = "Format";
    public const string Application = "Application";
    public const string ApplicationVersion = "ApplicationVersion";
    public const string ProjectTitle = "ProjectTitle";
    public const string TimelineCount = "TimelineCount";
    public const string TimelineNames = "TimelineNames";
    public const string TrackCount = "TrackCount";
    public const string ClipCount = "ClipCount";
    public const string ItemCount = "ItemCount";
    public const string MediaFileCount = "MediaFileCount";
    public const string FrameRate = "FrameRate";
    public const string Duration = "Duration";
    public const string Resolution = "Resolution";
    public const string IsTemplate = "IsTemplate";
    public const string Truncated = "Truncated";

    /// <summary>
    /// Extensions the plugin claims. Vegas Movie Studio <c>.vf</c> is deliberately absent: no genuine sample was
    /// available to confirm its layout, so the plugin does not guess at it (#1285).
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".prproj", ".aep", ".aepx", ".drp", ".drt", ".veg"];

    /// <summary>None of these project containers has a registered media type; selection is by extension.</summary>
    public static readonly IReadOnlyList<string> ContentTypes = [];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new(Kind, MediaMetadataType.Text),
        new(Format, MediaMetadataType.Text),
        new(Application, MediaMetadataType.Text),
        new(ApplicationVersion, MediaMetadataType.Text),
        new(ProjectTitle, MediaMetadataType.Text),
        new(TimelineCount, MediaMetadataType.Integer),
        new(TimelineNames, MediaMetadataType.Text),
        new(TrackCount, MediaMetadataType.Integer),
        new(ClipCount, MediaMetadataType.Integer),
        new(ItemCount, MediaMetadataType.Integer),
        new(MediaFileCount, MediaMetadataType.Integer),
        new(FrameRate, MediaMetadataType.Number, "fps"),
        new(Duration, MediaMetadataType.Number, "s"),
        new(Resolution, MediaMetadataType.Text),
        new(IsTemplate, MediaMetadataType.Boolean),
        new(Truncated, MediaMetadataType.Boolean)
    ]);
}
