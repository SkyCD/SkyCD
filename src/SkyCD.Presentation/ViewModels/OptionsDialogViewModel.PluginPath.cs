using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Plugins → Settings folder field: rescans the live plugin runtime only when the edit is committed —
/// Enter, the Browse picker, a lost focus or the dialog's OK — shows an inline hint instead of an error
/// dialog for a folder that does not exist, and reverts to the folder the dialog opened with when the
/// dialog is cancelled.
/// </summary>
public partial class OptionsDialogViewModel
{
    /// <summary>Folder the dialog was opened with, which Cancel must leave the runtime on.</summary>
    private string persistedPluginPath = string.Empty;
    /// <summary>Folder the live plugin runtime was last rescanned from.</summary>
    private string? scannedPluginPath;
    /// <summary>Host-supplied discovery pass; it runs off the UI thread and observes the token.</summary>
    private Func<string, CancellationToken, Task>? pluginPathScanner;
    private CancellationTokenSource? pluginPathScanCancellation;
    private Task currentPluginPathScan = Task.CompletedTask;
    private string? scanningPluginPath;

    [ObservableProperty] private string pluginPathHint = string.Empty;

    /// <summary>True while a discovery pass runs; the folder field shows the localized busy state.</summary>
    [ObservableProperty] private bool isPluginPathScanning;

    public bool HasPluginPathHint => !string.IsNullOrWhiteSpace(PluginPathHint);

    /// <summary>
    /// Supplies the host's discovery pass for one committed folder. Without it the field only hints and
    /// never scans; the host clears it when the dialog closes.
    /// </summary>
    public void SetPluginPathScanner(Func<string, CancellationToken, Task>? scanner) =>
        pluginPathScanner = scanner;

    /// <summary>
    /// Seeds the folder field when Options opens. The stored folder is the one the runtime already
    /// reflects, so the assignment itself neither rescans nor shows a hint.
    /// </summary>
    public void InitializePluginPath(string? storedPluginPath)
    {
        persistedPluginPath = storedPluginPath ?? string.Empty;
        scannedPluginPath = persistedPluginPath;
        PluginPath = persistedPluginPath;
        RefreshPluginPathHint();
    }

    /// <summary>Records the folder the runtime was just rescanned from.</summary>
    public void MarkPluginPathScanned(string? scannedPath)
    {
        scannedPluginPath = scannedPath ?? string.Empty;
        RefreshPluginPathHint();
    }

    /// <summary>Enter and a lost focus commit the typed folder to the runtime.</summary>
    [RelayCommand]
    private Task RescanPluginPathAsync() => CommitPluginPathAsync();

    /// <summary>Loads the plugin list when the dialog opens and after plugin actions change the folder contents.</summary>
    public Task ReloadPluginsAsync() => StartPluginPathScanAsync(force: true);

    /// <summary>
    /// Commits the typed folder. A scan already running for the same folder is awaited instead of
    /// restarted, so Enter followed by OK does not discover twice.
    /// </summary>
    public Task CommitPluginPathAsync() =>
        IsPluginPathScanning && string.Equals(scanningPluginPath, PluginPath, StringComparison.Ordinal)
            ? currentPluginPathScan
            : StartPluginPathScanAsync(force: false);

    /// <summary>
    /// Cancel path: a scan performed for a draft folder changes live plugin state, so that scan is
    /// abandoned first, then the field goes back to the stored folder and that folder is rescanned.
    /// </summary>
    public async Task RevertPluginPathPreviewAsync()
    {
        // A draft scan must not apply a folder the user just cancelled, so let it end before checking
        // whether the runtime still shows a preview.
        pluginPathScanCancellation?.Cancel();
        if (IsPluginPathScanning)
        {
            await currentPluginPathScan;
        }

        var runtimeShowsPreview = !string.Equals(scannedPluginPath, persistedPluginPath, StringComparison.Ordinal);
        PluginPath = persistedPluginPath;
        RefreshPluginPathHint();
        if (runtimeShowsPreview)
        {
            await StartPluginPathScanAsync(force: true);
        }
    }

    /// <summary>Drops the in-flight scan and its busy state; a scan already queued on the pool observes the token.</summary>
    internal void DisposePluginPathScan()
    {
        pluginPathScanCancellation?.Cancel();
        pluginPathScanCancellation = null;
        scanningPluginPath = null;
        IsPluginPathScanning = false;
    }

    partial void OnPluginPathChanged(string value)
    {
        RefreshPluginPathHint();
        // Typing supersedes the committed folder: the in-flight scan must not keep walking the old tree.
        pluginPathScanCancellation?.Cancel();
    }

    partial void OnPluginPathHintChanged(string value) => OnPropertyChanged(nameof(HasPluginPathHint));

    private async Task StartPluginPathScanAsync(bool force)
    {
        var scanner = pluginPathScanner;
        if (scanner is null)
        {
            return;
        }

        // A folder that does not exist leaves the list as it is; the hint under the field explains why.
        if (!force && !IsPluginPathRescanPending)
        {
            return;
        }

        // A superseding commit must not let the older result overwrite the newer one.
        pluginPathScanCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        pluginPathScanCancellation = cancellation;
        scanningPluginPath = PluginPath;
        IsPluginPathScanning = true;
        currentPluginPathScan = ScanPluginPathCoreAsync(scanner, PluginPath, cancellation);
        await currentPluginPathScan;
    }

    private async Task ScanPluginPathCoreAsync(
        Func<string, CancellationToken, Task> scanner,
        string path,
        CancellationTokenSource cancellation)
    {
        try
        {
            await scanner(path, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer commit or closed with the dialog; the newer scan owns the busy state.
        }
        finally
        {
            if (ReferenceEquals(pluginPathScanCancellation, cancellation))
            {
                pluginPathScanCancellation = null;
                scanningPluginPath = null;
                IsPluginPathScanning = false;
            }

            cancellation.Dispose();
        }
    }

    private bool IsPluginPathRescanPending =>
        IsExistingPluginFolder(PluginPath) &&
        !string.Equals(PluginPath, scannedPluginPath, StringComparison.Ordinal);

    private void RefreshPluginPathHint() => PluginPathHint = IsExistingPluginFolder(PluginPath)
        ? string.Empty
        : LocalizationService.Current.Get(UiKeys.PluginPathDirectoryMissing);

    /// <summary>
    /// Matches how the runtime reads the field: a path-separator list of folders that must all exist.
    /// </summary>
    internal static bool IsExistingPluginFolder(string? path)
    {
        var folders = SplitPluginPath(path);
        return folders.Length > 0 && folders.All(Directory.Exists);
    }

    /// <summary>
    /// First folder in the field's path-separator list that exists, or null when no entry resolves.
    /// The Browse picker opens here so it starts from what the user is already looking at.
    /// </summary>
    internal static string? ResolvePluginPathStartFolder(string? path) =>
        SplitPluginPath(path).FirstOrDefault(Directory.Exists);

    private static string[] SplitPluginPath(string? path) =>
        (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
