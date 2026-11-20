using System;
using Microsoft.Extensions.Logging;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Applies the scan completion settings once a scan and its import finished successfully. Every action is
/// best-effort: a failing ejector, sound or window activation is logged and never breaks scan completion.
/// Failed and cancelled scans must not call this type at all.
/// </summary>
public sealed class ScanCompletionService(
    IMediaEjector ejector,
    IUserNotificationPresenter notificationPresenter,
    ILogger<ScanCompletionService> logger)
{
    /// <param name="settings">Actions the user enabled for completed scans.</param>
    /// <param name="mediaSource">Drive root or optical device node of the scanned medium; <c>null</c> for folder scans.</param>
    public ScanCompletionResult Complete(ScanCompletionSettings settings, string? mediaSource = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var ejected = settings.EjectMediaAfterScan && !string.IsNullOrWhiteSpace(mediaSource)
            ? TryEject(mediaSource)
            : MediaEjectionOutcome.NotApplicable;

        var windowActivated = settings.ActivateMainWindowAfterScan &&
                              TryRun(static presenter => presenter.ActivateMainWindow(), "activate the main window");
        var soundPlayed = settings.PlaySoundAfterScan &&
                          TryRun(static presenter => presenter.PlayScanCompleteSound(), "play the scan completion sound");

        return new ScanCompletionResult(ejected, windowActivated, soundPlayed);
    }

    private MediaEjectionOutcome TryEject(string source)
    {
        try
        {
            // An unsupported platform or drive is an expected state, not a warning.
            return ejector.Eject(source);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ejecting the scanned medium '{Source}' failed; the scan itself succeeded.", source);
            return MediaEjectionOutcome.Failed;
        }
    }

    private bool TryRun(Action<IUserNotificationPresenter> action, string description)
    {
        try
        {
            action(notificationPresenter);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not {Description} after the scan finished.", description);
            return false;
        }
    }
}
