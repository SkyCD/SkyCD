namespace SkyCD.Presentation.Scanning;

/// <summary>Result of a best-effort media ejection.</summary>
public enum MediaEjectionOutcome
{
    /// <summary>The platform issued the eject request.</summary>
    Ejected,

    /// <summary>The source is not a drive or optical device.</summary>
    NotApplicable,

    /// <summary>The current platform has no media-ejection implementation.</summary>
    PlatformUnsupported,

    /// <summary>The platform supports ejection but the device or driver refused the request.</summary>
    Failed
}
