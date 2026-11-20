using System;
using SkyCD.Documents;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Immutable snapshot of the actions performed once a scan finishes successfully.
/// </summary>
public sealed record ScanCompletionSettings(
    bool EjectMediaAfterScan,
    bool ActivateMainWindowAfterScan,
    bool PlaySoundAfterScan)
{
    /// <summary>All completion actions are opt-in, so a catalogue run stays silent until the user asks for them.</summary>
    public static ScanCompletionSettings Default { get; } = new(
        EjectMediaAfterScan: false,
        ActivateMainWindowAfterScan: false,
        PlaySoundAfterScan: false);

    /// <summary>Returns <see langword="true"/> when at least one completion action is enabled.</summary>
    public bool HasAnyAction => EjectMediaAfterScan || ActivateMainWindowAfterScan || PlaySoundAfterScan;

    public static ScanCompletionSettings FromOptions(AppOptionsDocument options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ScanCompletionSettings(
            options.EjectMediaAfterScan,
            options.ActivateMainWindowAfterScan,
            options.PlaySoundAfterScan);
    }
}
