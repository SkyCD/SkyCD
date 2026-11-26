using SkyCD.SDK.Capabilities.Media;
using static SkyCD.SDK.Capabilities.Media.MediaMetadataType;

namespace SkyCD.Plugin.Metadata.Image.Models;

internal static class ContainerSchema
{
    public static MediaMetadataSchema Preview { get; } = new("skycd.image.container.preview", 1, []);

    public static MediaMetadataSchema Raw { get; } = new("skycd.image.raw.metadata", 1,
    [
        new("Format", Text), new("Width", Integer, "px"), new("Height", Integer, "px"),
        new("Orientation", Integer), new("RotationDegrees", Integer, "deg"), new("CapturedAt", Timestamp),
        new("CameraMake", Text), new("CameraModel", Text), new("LensModel", Text), new("Iso", Integer),
        new("Aperture", Text), new("ExposureTime", Text), new("FocalLength", Integer, "mm"),
        new("Software", Text), new("Tags", Text, Role: MediaMetadataFieldRole.Tags),
        new("Comment", Text), new("Rating", Integer)
    ]);

    public static MediaMetadataSchema Document { get; } = new("skycd.image.document.metadata", 1,
    [
        new("Format", Text), new("Width", Integer), new("Height", Integer), new("Title", Text),
        new("Author", Text), new("Software", Text), new("ColorMode", Text), new("BitDepth", Integer),
        new("LayerCount", Integer), new("CapturedAt", Timestamp), new("Tags", Text, Role: MediaMetadataFieldRole.Tags),
        new("Comment", Text), new("Rating", Integer)
    ]);
}
