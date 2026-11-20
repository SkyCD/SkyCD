namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Host-side effects of a finished scan: bringing the main window forward and signalling the user with a sound.
/// Implementations must marshal to the UI thread themselves; the core presenters are no-ops.
/// </summary>
public interface IUserNotificationPresenter
{
    void ActivateMainWindow();

    void PlayScanCompleteSound();
}
