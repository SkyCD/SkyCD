namespace SkyCD.Plugin.Metadata.Image.Models;

/// <summary>
/// Geometry and container facts read from an image header, before any pixels are decoded.
/// </summary>
/// <param name="StoredWidth">Width in pixels as encoded, before EXIF rotation.</param>
/// <param name="StoredHeight">Height in pixels as encoded, before EXIF rotation.</param>
/// <param name="Width">Display width in pixels, after applying EXIF rotation.</param>
/// <param name="Height">Display height in pixels, after applying EXIF rotation.</param>
/// <param name="Format">Container format in lowercase, for example <c>jpeg</c> or <c>png</c>.</param>
/// <param name="HasAlpha">True when the container can carry transparency.</param>
/// <param name="Orientation">EXIF orientation as stored, 1 to 8; 1 when absent.</param>
/// <param name="RotationDegrees">Clockwise rotation needed to display the image upright.</param>
/// <param name="FrameCount">Frames in the container; 1 for a still image.</param>
internal sealed record ImageProbeInfo(
    int StoredWidth,
    int StoredHeight,
    int Width,
    int Height,
    string Format,
    bool HasAlpha,
    int Orientation,
    int RotationDegrees,
    int FrameCount)
{
    public bool IsAnimated => FrameCount > 1;

    /// <summary>True when rotation swaps the stored axes, which is what the display dimensions reflect.</summary>
    public bool SwapsAxes => RotationDegrees is 90 or 270;
}
