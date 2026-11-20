namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Immutable snapshot of the scan settings edited on the options dialog.
/// </summary>
public sealed record ScanSettingsSnapshot(
    bool SkipSymbolicLinks,
    string HiddenSystemFilePolicy,
    bool SkipRecycleBins,
    bool SkipWellKnownSystemFolders,
    bool SkipEmptyFolders,
    bool SkipOnlineOnlyCloudFiles,
    bool ShowScanErrorsAfterScan,
    bool SaveScanErrorsToComments,
    bool EjectMediaAfterScan,
    bool ActivateMainWindowAfterScan,
    bool PlaySoundAfterScan);
