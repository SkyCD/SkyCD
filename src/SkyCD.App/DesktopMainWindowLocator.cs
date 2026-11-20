using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SkyCD.Presentation.Scanning;

namespace SkyCD.App;

/// <summary>
/// Applies scan completion effects on the desktop: brings the main window forward and plays the operating
/// system notification sound. The window is resolved on demand because this presenter is created before the
/// application lifetime reports its main window, and effects the platform cannot perform stay silent.
/// </summary>
public sealed class DesktopMainWindowLocator : IUserNotificationPresenter
{
    public void ActivateMainWindow()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ActivateCore();
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(ActivateCore);
    }

    public void PlayScanCompleteSound() => SystemNotificationSound.Play();

    private static void ActivateCore()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
