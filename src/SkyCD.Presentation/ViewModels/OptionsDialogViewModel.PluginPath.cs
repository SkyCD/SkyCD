using System;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Plugins → Settings folder field: rescans the live plugin runtime when typing pauses, when the field
/// loses focus or on Enter, shows an inline hint instead of an error dialog for a folder that does not
/// exist, and reverts to the folder the dialog opened with when the dialog is cancelled.
/// </summary>
public partial class OptionsDialogViewModel
{
    /// <summary>Typing pause before the folder field is rescanned; short enough to still feel immediate.</summary>
    internal static readonly TimeSpan PluginPathScanDelay = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider timeProvider;
    /// <summary>Folder the dialog was opened with, which Cancel must leave the runtime on.</summary>
    private string persistedPluginPath = string.Empty;
    /// <summary>Folder the live plugin runtime was last rescanned from.</summary>
    private string? scannedPluginPath;
    private ITimer? pluginPathScanTimer;

    [ObservableProperty] private string pluginPathHint = string.Empty;

    public bool HasPluginPathHint => !string.IsNullOrWhiteSpace(PluginPathHint);

    /// <summary>
    /// Raised when the live plugin runtime should be rescanned from <see cref="OptionsDialogViewModel.PluginPath"/>.
    /// </summary>
    public event EventHandler? PluginPathScanRequested;

    /// <summary>
    /// Seeds the folder field when Options opens. The stored folder is the one the runtime already
    /// reflects, so the assignment itself neither rescans nor shows a hint.
    /// </summary>
    public void InitializePluginPath(string? storedPluginPath)
    {
        CancelPendingPluginPathScan();
        persistedPluginPath = storedPluginPath ?? string.Empty;
        scannedPluginPath = persistedPluginPath;
        PluginPath = persistedPluginPath;
        RefreshPluginPathHint();
    }

    /// <summary>Records the folder the runtime was just rescanned from, which ends any pending debounce.</summary>
    public void MarkPluginPathScanned(string? scannedPath)
    {
        CancelPendingPluginPathScan();
        scannedPluginPath = scannedPath ?? string.Empty;
        RefreshPluginPathHint();
    }

    /// <summary>Rescans immediately: Enter, a lost focus and the Browse picker all end the typing pause.</summary>
    [RelayCommand]
    private void RescanPluginPath()
    {
        CancelPendingPluginPathScan();
        RequestPluginPathScan();
    }

    /// <summary>
    /// Cancel path: a preview scan performed while the dialog was open changed live plugin state, so the
    /// field goes back to the stored folder and that folder is rescanned.
    /// </summary>
    public void RevertPluginPathPreview()
    {
        var runtimeShowsPreview = !string.Equals(scannedPluginPath, persistedPluginPath, StringComparison.Ordinal);
        PluginPath = persistedPluginPath;
        CancelPendingPluginPathScan();
        RefreshPluginPathHint();
        if (runtimeShowsPreview)
        {
            PluginPathScanRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Hands the debounce timer back; a scan already queued on the pool is dropped by <see cref="disposed"/>.</summary>
    internal void DisposePluginPathScan()
    {
        CancelPendingPluginPathScan();
        pluginPathScanTimer?.Dispose();
        pluginPathScanTimer = null;
    }

    partial void OnPluginPathChanged(string value)
    {
        RefreshPluginPathHint();
        if (string.Equals(value, scannedPluginPath, StringComparison.Ordinal))
        {
            CancelPendingPluginPathScan();
            return;
        }

        // Every edit restarts the pause, so keystroke-by-keystroke typing supersedes the pending scan
        // instead of queueing one scan per character.
        SchedulePluginPathScan();
    }

    partial void OnPluginPathHintChanged(string value) => OnPropertyChanged(nameof(HasPluginPathHint));

    private void SchedulePluginPathScan()
    {
        pluginPathScanTimer ??= timeProvider.CreateTimer(
            static state => ((OptionsDialogViewModel)state!).RequestPluginPathScan(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        pluginPathScanTimer.Change(PluginPathScanDelay, Timeout.InfiniteTimeSpan);
    }

    private void RequestPluginPathScan()
    {
        // A folder that does not exist leaves the list as it is; the hint under the field explains why.
        if (disposed || !IsPluginPathRescanPending)
        {
            return;
        }

        PluginPathScanRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool IsPluginPathRescanPending =>
        IsExistingPluginFolder(PluginPath) &&
        !string.Equals(PluginPath, scannedPluginPath, StringComparison.Ordinal);

    private void CancelPendingPluginPathScan() =>
        pluginPathScanTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void RefreshPluginPathHint() => PluginPathHint = IsExistingPluginFolder(PluginPath)
        ? string.Empty
        : LocalizationService.Current.Get(UiKeys.PluginPathDirectoryMissing);

    /// <summary>
    /// Matches how the runtime reads the field: a path-separator list of folders that must all exist.
    /// </summary>
    internal static bool IsExistingPluginFolder(string? path)
    {
        var folders = (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return folders.Length > 0 && folders.All(Directory.Exists);
    }
}
