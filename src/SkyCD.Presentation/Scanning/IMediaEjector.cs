namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Best-effort media ejection for a scanned source. Implementations must never throw for unsupported media or
/// platforms; the result describes what happened so callers can log without interrupting scan completion.
/// </summary>
public interface IMediaEjector
{
    /// <summary>Whether the current platform has any media-ejection implementation at all.</summary>
    bool IsPlatformSupported { get; }

    /// <summary>
    /// Describes what <see cref="Eject"/> would attempt for <paramref name="source"/>, so the options dialog can
    /// tell the user where the setting applies.
    /// </summary>
    MediaEjectionSupport GetSupport(string? source);

    /// <summary>Attempts to eject the medium behind the scanned source (a drive root or an optical device node).</summary>
    MediaEjectionOutcome Eject(string? source);
}
