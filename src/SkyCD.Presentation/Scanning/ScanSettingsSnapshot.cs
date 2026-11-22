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
    bool IncludeExtendedInfo,
    bool ShowScanErrorsAfterScan,
    bool SaveScanErrorsToComments,
    bool EjectMediaAfterScan,
    bool ActivateMainWindowAfterScan,
    bool PlaySoundAfterScan,
    string ScanConcurrencyMode = "Automatic",
    int ScanConcurrencyManualWorkers = 2,
    int ScanConcurrencyCpuSharePercent = 80,
    int ScanConcurrencyLoadShrinkPercent = 70,
    int ScanConcurrencyLoadSuspendPercent = 85);
