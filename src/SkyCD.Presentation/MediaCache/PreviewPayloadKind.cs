namespace SkyCD.Presentation.MediaCache;

/// <summary>How the resolved preview payload of an entry should be rendered.</summary>
public enum PreviewPayloadKind
{
    /// <summary>No artifact is available; typed fields may still be shown.</summary>
    None = 0,

    /// <summary>A raster image such as a cover, poster, document page or thumbnail.</summary>
    Image,

    /// <summary>A bounded plain-text body decoded from the stored artifact.</summary>
    Text
}
