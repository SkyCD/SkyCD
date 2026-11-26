using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void OnOpened(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(OpenedAsync);

    private async Task OpenedAsync()
    {
        if (isSessionStateLoaded || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var options = LoadAppOptions();
        if (isWindowPlacementDeferred)
        {
            // Screen geometry was not queryable before the first show; apply the placement now.
            ApplyWindowBounds(options);
        }

        vm.ApplySessionState(
            options.Browser.ViewMode,
            options.Browser.SortMode,
            options.IsStatusBarVisible,
            options.Browser.SortDescending);
        vm.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
        vm.SetStatusVariants(statusVariantRepository.GetOrdered());
        ApplyLanguage(options.Language);
        if (!string.IsNullOrWhiteSpace(options.LastOpenedCatalogPath) && File.Exists(options.LastOpenedCatalogPath))
        {
            // Advertise the restore before the first frame so the shell paints with its busy state, then wait
            // for that frame: plugin discovery and the import would otherwise block rendering and leave the
            // window as a transparent client area (#1109).
            vm.BeginCatalogLoad();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            try
            {
                EnsureFileFormatProvidersLoaded();
                await catalogFiles.LoadAsync(options.LastOpenedCatalogPath);
            }
            catch (Exception exception)
            {
                vm.EndCatalogLoad();
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalog, exception.Message);
            }
        }

        if (Application.Current is App app)
        {
            await app.BackgroundServicesStarted;
        }

        RefreshMcpStatusIndicator();

        isSessionStateLoaded = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e) =>
        _ = RunUiActionAsync(() => ClosingAsync(e));

    private async Task ClosingAsync(WindowClosingEventArgs e)
    {
        if (isCompletingConfirmedClose)
        {
            return;
        }

        if (catalogFiles.LoadTask is { IsCompleted: false } loading)
        {
            // Keep the window responsive, and let the database operation finish
            // before shutdown disposes its repositories.
            e.Cancel = true;
            try { await loading; }
            catch (Exception) { /* The open/startup handler displays the failure. */ }
            Close();
            return;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.IsDirtyDocument)
        {
            e.Cancel = true;
            var decision = await catalogFiles.ShowUnsavedChangesPromptAsync();
            if (decision == UnsavedChangesDecision.Cancel)
            {
                return;
            }

            if (decision == UnsavedChangesDecision.Save && !await catalogFiles.SaveCatalogAsync(vm))
            {
                return;
            }

            sessionState.Save(this, TreePaneColumn, vm);
            isCompletingConfirmedClose = true;
            Close();
            return;
        }

        try
        {
            sessionState.Save(this, TreePaneColumn, vm);
        }
        catch
        {
            e.Cancel = true;
            throw;
        }
    }
}
