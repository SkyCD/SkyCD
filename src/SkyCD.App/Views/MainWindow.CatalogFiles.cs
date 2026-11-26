using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using DryIoc;
using Microsoft.Extensions.Localization;
using SkyCD.Core.DependencyInjection;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.Catalog.Export;
using SkyCD.Presentation.Catalog.Split;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void OnNewCatalogRequested(object? sender, EventArgs e) => _ = RunUiActionAsync(catalogFiles.NewAsync);

    private void OnOpenCatalogRequested(object? sender, EventArgs e) => _ = RunUiActionAsync(catalogFiles.OpenAsync);

    private void OnSaveCatalogRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(SaveCatalogRequestedAsync);

    private async Task SaveCatalogRequestedAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await catalogFiles.SaveCatalogAsync(vm);
        }
    }

    private void OnSaveCatalogAsRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(SaveCatalogAsRequestedAsync);

    private async Task SaveCatalogAsRequestedAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await catalogFiles.SaveCatalogAsync(vm, saveAs: true);
        }
    }

    private void OnSplitCatalogRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(SplitCatalogRequestedAsync);

    private async Task SplitCatalogRequestedAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await catalogFiles.SplitCatalogAsync(vm);
        }
    }

    private void OnExportCatalogRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(ExportCatalogRequestedAsync);

    private async Task ExportCatalogRequestedAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await catalogFiles.ExportCatalogAsync(vm);
        }
    }

    private sealed class CatalogFileActions(MainWindow window)
    {
        private static readonly IStringLocalizer PickerLocalizer = new PropertyValueLocalizer();

        public Task? LoadTask { get; private set; }

        public async Task NewAsync()
        {
            if (window.DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (vm.IsDirtyDocument)
            {
                var decision = await ShowUnsavedChangesPromptAsync();
                if (decision == UnsavedChangesDecision.Cancel)
                {
                    return;
                }

                if (decision == UnsavedChangesDecision.Save && !await SaveCatalogAsync(vm))
                {
                    return;
                }
            }

            // The new catalog runs on the shared working database, so the previous catalog's background sync must
            // stop before that database is cleared and handed to the new view model.
            var resumeBackgroundSync = vm.IsBackgroundMediaSyncEnabled;
            vm.IsBackgroundMediaSyncEnabled = false;
            MainWindowViewModel? newCatalog = null;
            try
            {
                newCatalog = MainWindowViewModel.CreateNewCatalog(window.repositoryManager);
                newCatalog.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
                newCatalog.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                    ServiceProvider.Resolve<VolumeExtensionManager>(),
                    ServiceProvider.Resolve<IndexWorkItemSequence>(),
                    ServiceProvider.Resolve<IAudioCdMetadataProvider>());
                newCatalog.RefreshDescriptionServices(ServiceProvider.Resolve<DescriptionExtensionManager>());
                newCatalog.ApplySessionState(vm.CurrentViewMode, vm.CurrentSortMode, vm.IsStatusBarVisible, vm.IsSortDescending);
                newCatalog.ApplyDetailsColumnSettings(vm.GetDetailsColumnSettings());
            }
            catch (Exception ex)
            {
                newCatalog?.Dispose();
                vm.IsBackgroundMediaSyncEnabled = resumeBackgroundSync;
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToCreateNewCatalog,
                    ex.GetBaseException().Message);
                return;
            }

            window.DataContext = newCatalog;
        }

        public async Task OpenAsync()
        {
            if (window.DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            window.EnsureFileFormatProvidersLoaded();

            if (vm.IsDirtyDocument)
            {
                var decision = await ShowUnsavedChangesPromptAsync();
                if (decision == UnsavedChangesDecision.Cancel)
                {
                    return;
                }

                if (decision == UnsavedChangesDecision.Save && !await SaveCatalogAsync(vm))
                {
                    return;
                }
            }

            var fileTypeChoices = window.fileFormatManager.GetOpenFilters()
                .ToFilePickerTypes(
                    allSupportedFilesLabel: PickerLocalizer["AllSupportedFiles"].Value,
                    allFilesLabel: PickerLocalizer["AllFiles"].Value);

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationService.Current.Get(TextKeys.OpenCatalog),
                AllowMultiple = false,
                FileTypeFilter = fileTypeChoices
            });

            var localPath = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return;
            }

            try
            {
                await LoadAsync(localPath);
            }
            catch (UnsupportedFileFormatException)
            {
                var extension = Path.GetExtension(localPath);
                var readableExtensions = window.fileFormatManager.GetReadableFormats()
                    .SelectMany(static format => format.Extensions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var supportedText = readableExtensions.Length == 0
                    ? LocalizationService.Current.Get(TextKeys.None)
                    : string.Join(", ", readableExtensions);

                vm.StatusText = string.IsNullOrWhiteSpace(extension)
                    ? LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogUnsupportedFileFormatReadableExtensions, supportedText)
                    : LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogNoReadableHandlerMappedForReadableExtensions, extension, supportedText);
            }
            catch (FileFormatHandlerResolutionException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.FailedToOpenCatalogFileFormatHandlerIsUnavailableCheckPluginSettings);
            }
            catch (FileFormatNotReadableException ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogPluginCannotReadThisFormat, ex.Message);
            }
            catch (FileFormatReadFailedException ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalogPluginReadError, ex.Message);
            }
            catch (Exception ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToOpenCatalog, ex.Message);
            }
        }

        public async Task<bool> SaveCatalogAsync(MainWindowViewModel vm, bool saveAs = false)
        {
            var targetPath = saveAs ? null : vm.CurrentCatalogPath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                var options = CatalogSavePicker.Create(
                    ensureFileFormatManager: () =>
                    {
                        window.EnsureFileFormatProvidersLoaded();
                        return window.fileFormatManager;
                    },
                    saveAs,
                    LocalizationService.Current.Get(saveAs ? TextKeys.SaveCatalogAs : TextKeys.SaveCatalog),
                    saveAs ? PickerLocalizer["AllFiles"].Value : null);
                if (options is null)
                {
                    vm.StatusText = LocalizationService.Current.Get(
                        TextKeys.FailedToSaveCatalogNoWritableFileFormatCheckPluginSettings);
                    return false;
                }

                var file = await window.StorageProvider.SaveFilePickerAsync(options);
                targetPath = file?.TryGetLocalPath();
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            try
            {
                var scopeItems = CatalogScopeDataService.Collect(
                    window.virtualCollectionRepository.GetOrdered(),
                    window.statusVariantRepository.GetOrdered(),
                    window.scanExclusionRuleRepository.GetOrdered(),
                    window.aiScanExclusionRepository.GetOrdered(),
                    window.pluginManager.GetCapabilities<ICatalogAuxiliaryDataProvider>());
                var saved = await new CatalogSaveService(window.fileFormatManager).SaveAsync(
                    targetPath,
                    vm.GetAllCatalogEntries(),
                    scopeItems,
                    async preview =>
                    {
                        var dialog = new SaveDataLossWindow(preview.FormatDisplayName,
                            CatalogDataLossText.BuildLines(preview.Items));
                        return await dialog.ShowDialog<bool>(window);
                    });
                if (!saved)
                {
                    return false;
                }

                if (saveAs) vm.CompleteSaveCatalogAs(targetPath);
                else vm.CompleteSaveCatalog(targetPath);
                return true;
            }
            catch (CatalogSaveIOException ex)
            {
                // Name the folder and the way forward instead of the internal temp file the save never created.
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToSaveCatalogAccessDenied, ex.DirectoryPath);
                if (!saveAs)
                {
                    return await SaveCatalogAsync(vm, saveAs: true);
                }

                return false;
            }
            catch (Exception ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToSaveCatalog, ex.Message);
                return false;
            }
        }

        public async Task SplitCatalogAsync(MainWindowViewModel vm)
        {
            window.EnsureFileFormatProvidersLoaded();
            var entries = vm.GetAllCatalogEntries();
            if (entries.Count == 0)
            {
                vm.StatusText = LocalizationService.Current.Get(UiKeys.SplitNoEntries);
                return;
            }

            var formats = window.fileFormatManager.GetSaveFormats()
                .Where(static format => format.CanRead)
                .Select(static format => new SplitFormatOption(
                    format.FormatId,
                    format.DisplayName,
                    format.Extensions.Select(static extension => extension.Trim().TrimStart('.'))
                        .FirstOrDefault(static extension => !string.IsNullOrWhiteSpace(extension)) ?? "dat",
                    format.Extensions.ToArray()))
                .ToList();
            if (formats.Count == 0)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.SplitNoWritableFormat);
                return;
            }

            var defaultFormatId = ResolveSplitFormatId(vm.CurrentCatalogPath, formats);
            var sourcePath = vm.CurrentCatalogPath;
            SplitWindow? splitWindow = null;
            var viewModel = new SplitDialogViewModel(
                string.IsNullOrWhiteSpace(sourcePath)
                    ? "catalog"
                    : Path.GetFileNameWithoutExtension(sourcePath),
                entries.Count,
                string.IsNullOrWhiteSpace(sourcePath)
                    ? string.Empty
                    : Path.GetDirectoryName(sourcePath) ?? string.Empty,
                formats,
                defaultFormatId,
                (request, progress, cancellationToken) =>
                {
                    var scopeItems = CatalogScopeDataService.Collect(
                        window.virtualCollectionRepository.GetOrdered(),
                        window.statusVariantRepository.GetOrdered(),
                        window.scanExclusionRuleRepository.GetOrdered(),
                        window.aiScanExclusionRepository.GetOrdered(),
                        window.pluginManager.GetCapabilities<ICatalogAuxiliaryDataProvider>());
                    return new CatalogSplitService(window.fileFormatManager).SplitAsync(
                        entries,
                        scopeItems,
                        request,
                        async preview =>
                        {
                            var dialog = new SaveDataLossWindow(preview.FormatDisplayName,
                                CatalogDataLossText.BuildLines(preview.Items));
                            return await dialog.ShowDialog<bool>(splitWindow!);
                        },
                        progress,
                        cancellationToken);
                });
            splitWindow = new SplitWindow { DataContext = viewModel };
            await splitWindow.ShowDialog(window);
            if (viewModel.OutcomeMessage is { } outcome)
            {
                vm.StatusText = outcome;
            }
        }

        public async Task ExportCatalogAsync(MainWindowViewModel vm)
        {
            window.EnsureFileFormatProvidersLoaded();
            var formats = window.fileFormatManager.GetExportFormats()
                .Select(static format => new ExportFormatOption(
                    format.FormatId,
                    format.DisplayName,
                    format.Extensions.Select(static extension => extension.Trim().TrimStart('.'))
                        .FirstOrDefault(static extension => !string.IsNullOrWhiteSpace(extension)) ?? "dat",
                    format.Extensions.ToArray()))
                .ToList();
            if (formats.Count == 0)
            {
                vm.StatusText = LocalizationService.Current.Get(
                    TextKeys.FailedToExportCatalogNoExportFormatCheckPluginSettings);
                return;
            }

            var dialogViewModel = new ExportDialogViewModel(formats);
            var dialog = new ExportWindow { DataContext = dialogViewModel };
            await dialog.ShowDialog(window);
            if (dialogViewModel.AcceptedFormat is not { } selected)
            {
                return;
            }

            var options = new FilePickerSaveOptions
            {
                Title = LocalizationService.Current.Get(TextKeys.ExportCatalog),
                SuggestedFileName = $"catalog.{selected.DefaultExtension}",
                DefaultExtension = selected.DefaultExtension,
                FileTypeChoices = window.fileFormatManager.GetExportFiltersFor(selected.FormatId)
                    .ToFilePickerTypes(allSupportedFilesLabel: null, allFilesLabel: PickerLocalizer["AllFiles"].Value)
            };
            var file = await window.StorageProvider.SaveFilePickerAsync(options);
            var targetPath = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            try
            {
                var scopeItems = CatalogScopeDataService.Collect(
                    window.virtualCollectionRepository.GetOrdered(),
                    window.statusVariantRepository.GetOrdered(),
                    window.scanExclusionRuleRepository.GetOrdered(),
                    window.aiScanExclusionRepository.GetOrdered(),
                    window.pluginManager.GetCapabilities<ICatalogAuxiliaryDataProvider>());
                var saved = await new CatalogSaveService(window.fileFormatManager).SaveAsync(
                    targetPath,
                    vm.GetAllCatalogEntries(),
                    scopeItems,
                    async preview =>
                    {
                        var lossDialog = new SaveDataLossWindow(preview.FormatDisplayName,
                            CatalogDataLossText.BuildLines(preview.Items));
                        return await lossDialog.ShowDialog<bool>(window);
                    });
                if (!saved)
                {
                    return;
                }

                vm.StatusText = LocalizationService.Current.Format(TextKeys.ExportedCatalogTo,
                    Path.GetFileName(targetPath));
            }
            catch (CatalogSaveIOException ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToExportCatalogAccessDenied,
                    ex.DirectoryPath);
            }
            catch (Exception ex)
            {
                vm.StatusText = LocalizationService.Current.Format(TextKeys.FailedToExportCatalog, ex.Message);
            }
        }

        private string ResolveSplitFormatId(string? currentCatalogPath, IReadOnlyList<SplitFormatOption> formats)
        {
            if (!string.IsNullOrWhiteSpace(currentCatalogPath))
            {
                try
                {
                    var resolved = window.fileFormatManager.ResolveFormatId(null, currentCatalogPath, forWrite: true);
                    if (formats.Any(option =>
                            option.FormatId.Equals(resolved, StringComparison.OrdinalIgnoreCase)))
                    {
                        return resolved;
                    }
                }
                catch (Exception exception) when (exception is FileFormatHandlerResolutionException
                    or UnsupportedFileFormatException)
                {
                    // Fall through to the first writable format below.
                }
            }

            return formats[0].FormatId;
        }

        public async Task<UnsavedChangesDecision> ShowUnsavedChangesPromptAsync()
        {
            var dialog = new UnsavedChangesWindow();
            var result = await dialog.ShowDialog<UnsavedChangesDecision?>(window);
            return result ?? UnsavedChangesDecision.Cancel;
        }

        public Task LoadAsync(string localPath)
        {
            if (LoadTask is { IsCompleted: false } loading) return loading;
            return LoadTask = LoadCatalogAsync(localPath);
        }

        private async Task LoadCatalogAsync(string localPath)
        {
            if (window.DataContext is not MainWindowViewModel vm) return;
            vm.BeginCatalogLoad();
            var progress = new Progress<int>(vm.ReportCatalogLoadProgress);
            MainWindowViewModel? reloadedViewModel = null;
            IReadOnlyList<string> importWarnings = [];
            // Import hooks reach plugin storage through the open-catalog state, so the repository and path must
            // be visible before the load callback runs. Update them on the UI thread to keep plugin event
            // handlers, which refresh decorations, off the worker thread.
            var previousCatalogPath = window.openCatalogState.Path;
            window.openCatalogState.Path = localPath;
            window.openCatalogState.Attach(window.catalogRepository);
            var loaded = false;
            try
            {
                reloadedViewModel = await new CatalogLoadService(window.fileFormatManager).LoadAsync(localPath, (payload, importProgress) =>
                {
                    var entries = ExtractCatalogEntries(payload);
                    DatabaseGate.InBatch(window.catalogRepository.Collection.Database, () =>
                    {
                        ReplaceCatalogContent(entries, importProgress);
                    });

                    // The plugin sees the entries after they are written, so its own storage and warnings stay
                    // a plugin concern; the host only folds the warnings into the status line.
                    var byId = entries
                        .GroupBy(static entry => entry.Id, StringComparer.Ordinal)
                        .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
                    var imported = entries.Select(entry => CatalogApiAdapter.Project(entry, byId)).ToArray();
                    importWarnings = ServiceProvider.Resolve<CatalogImportExtensionManager>()
                        .RunAsync(imported, TimeSpan.FromSeconds(30))
                        .GetAwaiter()
                        .GetResult()
                        .Warnings;
                    importProgress.Report(90);
                    return new MainWindowViewModel(window.repositoryManager);
                }, progress);
                reloadedViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
                reloadedViewModel.RefreshCatalogFeatureServices(ServiceProvider.Resolve<CatalogFeatureExtensionManager>());
                reloadedViewModel.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                    ServiceProvider.Resolve<VolumeExtensionManager>(),
                    ServiceProvider.Resolve<IndexWorkItemSequence>(),
                    ServiceProvider.Resolve<IAudioCdMetadataProvider>());
                var options = window.LoadAppOptions();
                reloadedViewModel.ApplySessionState(options.Browser.ViewMode, options.Browser.SortMode, options.IsStatusBarVisible,
                    options.Browser.SortDescending);
                reloadedViewModel.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
                reloadedViewModel.CurrentCatalogPath = localPath;
                options.LastOpenedCatalogPath = localPath;
                window.SaveAppOptions(options);
                reloadedViewModel.CompleteOpenCatalog();
                if (importWarnings.Count > 0)
                {
                    reloadedViewModel.StatusText = LocalizationService.Current.Format(UiKeys.PluginImportWarnings, string.Join("; ", importWarnings));
                }
                reloadedViewModel.AttachOpenCatalogState(window.openCatalogState);
                window.DataContext = reloadedViewModel;
                reloadedViewModel = null; // Ownership passes to the window.
                loaded = true;
            }
            finally
            {
                if (!loaded)
                {
                    window.openCatalogState.Path = previousCatalogPath;
                }

                reloadedViewModel?.Dispose();
                vm.EndCatalogLoad();
            }
        }

        private void ReplaceCatalogContent(IReadOnlyList<CatalogDocument> entries, IProgress<int>? progress = null)
        {
            var existingEntries = window.catalogRepository.GetAll();
            var total = Math.Max(1, existingEntries.Count + entries.Count);
            var processed = 0;
            foreach (var existing in existingEntries)
            {
                using var document = window.catalogRepository.Collection.GetDocument(existing.Id);
                if (document is not null)
                {
                    window.catalogRepository.Collection.Delete(document);
                }
                progress?.Report(++processed * 90 / total);
            }
            window.catalogRepository.InvalidateSnapshot();

            foreach (var entry in entries)
            {
                window.catalogRepository.Save(entry.Id, entry);
                progress?.Report(++processed * 90 / total);
            }

            // Imported rows may carry source-specific location data (PhysicalLocation, ZdcStorageLocation);
            // canonicalize it into the location model now that the entries are persisted. The raw source
            // keys stay in Properties so export round-trips remain lossless.
            if (CreateLocationService() is { } locationService)
            {
                foreach (var entry in entries)
                {
                    locationService.CanonicalizeImportedLocation(entry);
                }
            }
        }

        /// <summary>
        /// Builds a location service on the catalog database for import canonicalization without
        /// changing the window constructor surface.
        /// </summary>
        private SkyCD.Presentation.Locations.CatalogLocationService? CreateLocationService()
        {
            try
            {
                if (window.repositoryManager.For<LocationDocument>() is not LocationDocumentRepository locationRepository)
                {
                    return null;
                }

                return new SkyCD.Presentation.Locations.CatalogLocationService(window.catalogRepository, locationRepository);
            }
            catch
            {
                return null;
            }
        }
    }
}
