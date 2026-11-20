namespace SkyCD.Presentation.Scanning;

/// <summary>What happened when the scan completion settings were applied.</summary>
public sealed record ScanCompletionResult(
    MediaEjectionOutcome MediaEjection,
    bool MainWindowActivated,
    bool SoundPlayed);
