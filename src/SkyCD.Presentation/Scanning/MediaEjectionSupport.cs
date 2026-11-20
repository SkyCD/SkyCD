namespace SkyCD.Presentation.Scanning;

/// <summary>Where the media-ejection option is expected to work for a given scan source.</summary>
public enum MediaEjectionSupport
{
    /// <summary>The current platform has no media-ejection implementation.</summary>
    PlatformUnsupported,

    /// <summary>The source is not a drive or optical device (for example a folder or a network location).</summary>
    SourceUnsupported,

    /// <summary>The source is a drive or optical device the platform can eject.</summary>
    Supported
}
