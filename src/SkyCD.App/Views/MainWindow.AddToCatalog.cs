using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SkyCD.Core.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.Accounts;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void TrackAddingDialog(AddingProgressDialogViewModel progressVm) => openAddingDialogs.Add(progressVm);

    private void UntrackAddingDialog(AddingProgressDialogViewModel progressVm) => openAddingDialogs.Remove(progressVm);

    /// <summary>
    /// Closes the Adding dialog when the run is clean; with errors logged it stays open and the footer
    /// button becomes Close so the log can be read before dismissing it.
    /// </summary>
    private void FinishAddingDialog(AddingProgressDialogViewModel progressVm, AddingProgressWindow progressDialog)
    {
        progressVm.FlushPendingReports();
        progressVm.FinishCancellation();
        UntrackAddingDialog(progressVm);

        if (progressVm.HasErrors)
        {
            progressVm.IsCloseMode = true;
            return;
        }

        progressDialog.Close();
    }

    private void OnAddToListRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(AddToListRequestedAsync);

    private async Task AddToListRequestedAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await ShowAddToListDialogAsync(vm);
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        var canAccept = DataContext is MainWindowViewModel { IsCatalogInteractionEnabled: true } && HasDropPayload(e);
        e.DragEffects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        if (canAccept)
        {
            UpdateDropFeedback(e);
        }
        else
        {
            ClearDropFeedback(clearDragDrives: false);
        }

        e.Handled = true;
    }

    private void OnWindowDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropFeedback(clearDragDrives: true);
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e) =>
        _ = RunUiActionAsync(() => WindowDropAsync(e));

    private async Task WindowDropAsync(DragEventArgs e)
    {
        e.Handled = true;

        // The destination must follow the pointer, not whatever the tree selection happens to be
        // when the dialog is confirmed.
        UpdateDropFeedback(e);
        var destination = dropDestination;
        var (paths, text) = ReadDropPayload(e);
        ClearDropFeedback(clearDragDrives: false);

        if (DataContext is not MainWindowViewModel { IsCatalogInteractionEnabled: true } vm)
        {
            dragDrives = null;
            return;
        }

        // Drive enumeration can stall on network volumes; never run it on the UI thread (#1060).
        var drives = dragDrives ??= await Task.Run(() => mediaDriveProvider.GetSupportedDrives());
        dragDrives = null;
        var sources = DropSourceResolver.Resolve(paths, text, drives);
        if (sources.Count == 0)
        {
            return;
        }

        await ShowAddToListDialogAsync(vm, sources, destination);
    }

    private static bool HasDropPayload(DragEventArgs e) =>
        (e.DataTransfer.TryGetFiles()?.Any(item => item is IStorageFolder) ?? false) ||
        DropSourceResolver.TryResolveUrl(e.DataTransfer.TryGetText()) is not null;

    private static (IReadOnlyList<DroppedPath> Paths, string? Text) ReadDropPayload(DragEventArgs e)
    {
        var paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => new DroppedPath(item.TryGetLocalPath() ?? string.Empty, item is IStorageFolder))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .ToList();
        return (paths, e.DataTransfer.TryGetText());
    }

    /// <summary>
    /// Highlights the container under the pointer and records it as the drop parent. Containers are
    /// tree nodes and folder-like browser items; over anything else the drop lands on the library root.
    /// </summary>
    private void UpdateDropFeedback(DragEventArgs e)
    {
        Control? container = null;
        var destination = DropDestination.LibraryRoot;

        var hit = this.InputHitTest(e.GetPosition(this)) as Visual;
        if (hit is not null)
        {
            if (IsWithin(hit, MainTreeView) &&
                FindAncestor<TreeViewItem>(hit) is { DataContext: BrowserTreeNode node } treeItem)
            {
                container = treeItem;
                destination = DropDestination.FromTreeNode(node.Key, node.Title);
            }
            else if (IsWithin(hit, MainBrowserItemsView) &&
                     FindAncestor<ListBoxItem>(hit) is { DataContext: CatalogDocument document } listItem &&
                     DropDestination.FromCatalogItem(document.Id, document.DisplayName, document.Type) is { } itemDestination)
            {
                container = listItem;
                destination = itemDestination;
            }
        }

        if (!ReferenceEquals(dropTargetContainer, container))
        {
            dropTargetContainer?.Classes.Remove("drop-target");
            dropTargetContainer = container;
            container?.Classes.Add("drop-target");
        }

        dropDestination = destination;
        // The chip names the implicit parent only when no visible container is highlighted.
        DropHint.IsVisible = container is null;
    }

    private void ClearDropFeedback(bool clearDragDrives)
    {
        dropTargetContainer?.Classes.Remove("drop-target");
        dropTargetContainer = null;
        dropDestination = DropDestination.LibraryRoot;
        DropHint.IsVisible = false;
        if (clearDragDrives)
        {
            dragDrives = null;
        }
    }

    private static bool IsWithin(Visual? visual, Visual? ancestor)
    {
        var current = visual;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private async Task ShowAddToListDialogAsync(
        MainWindowViewModel vm,
        IReadOnlyList<DroppedSource>? droppedSources = null,
        DropDestination? dropDestination = null)
    {
        var targetParent = dropDestination ?? (vm.SelectedTreeNode is { } node
            ? DropDestination.FromTreeNode(node.Key, node.Title)
            : DropDestination.LibraryRoot);
        var dialogVm = new AddToListDialogViewModel(
            mediaDriveProvider,
            locationOptions: vm.GetLocationChoices(),
            accountSessions: pluginManager.GetCapabilities<IAccountSession>(),
            captureProviders: pluginManager.GetCapabilities<ISourceFactoryPluginCapability>());
        dialogVm.SetTargetParent(targetParent.ParentId, targetParent.DisplayName);
        if (droppedSources is { Count: > 0 })
        {
            dialogVm.ApplyDroppedSources(droppedSources);
        }

        // The account list comes from this catalog's folders; a different catalog starts empty.
        dialogVm.SetCatalogAccountFolders(vm.GetAllCatalogEntries());
        // Connector status comes from local token-free snapshots, so the picker is ready when the dialog opens.
        await dialogVm.RefreshAccountsAsync();

        var dialog = new AddToListWindow
        {
            DataContext = dialogVm
        };

        var accepted = await dialog.ShowDialog<bool?>(this);
        if (accepted != true)
        {
            dialogVm.ClearInternetSecrets();
            return;
        }

        var targetParentId = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
            ? null
            : dialogVm.TargetParentId ?? DropDestination.LibraryRootId;

        if (dialogVm.SourceMode == AddToListSourceMode.CaptureSource)
        {
            try
            {
                // The plugin owns the capture source: it persists the draft, starts the loop and reports
                // frames through the host capture API, so the dialog only routes the confirmed input.
                await dialogVm.AddCaptureSourceAsync();
            }
            catch (Exception ex)
            {
                vm.StatusText = SecretSanitizer.Sanitize(ex.Message);
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.FromAccount)
        {
            if (dialogVm.SelectedAccount is not { } account)
            {
                return;
            }

            try
            {
                var documents = AccountCatalogBuilder.BuildDocuments(account.Plan, targetParentId);
                await vm.AddImportedDocumentsAsync(documents, CancellationToken.None);
                vm.StatusText = LocalizationService.Current.Format(TextKeys.Added, account.Plan.FolderName);
            }
            catch (Exception ex)
            {
                vm.StatusText = SecretSanitizer.Sanitize(ex.Message);
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Internet)
        {
            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceProfile.Network);
            var scanOptions = dialogVm.CreateInternetScanOptions(
                targetParentId,
                scanTraversal,
                includeExtendedInfo: scanErrorOptions.IncludeExtendedInfo);
            dialogVm.ClearInternetSecrets();

            var scanner = new RemoteCatalogScanner(ServiceProvider.Resolve<IMediaTargetMetadataProvider>());
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Internet
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);
            TrackAddingDialog(progressVm);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 80);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 90);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                var message = SecretSanitizer.Sanitize(ex.Message);
                progressVm.ReportError(message);
                vm.StatusText = message;
            }
            finally
            {
                FinishAddingDialog(progressVm, progressDialog);
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, mediaSource: null);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.TitleOnly)
        {
            try
            {
                vm.AddTitleOnlyMedia(dialogVm.MediaName, targetParentId, dialogVm.SelectedLocationId);
            }
            catch (Exception ex)
            {
                vm.StatusText = ex.Message;
            }

            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Media)
        {
            var rootType = CatalogDocumentType.Media;

            var selectedDrivePath = dialogVm.SelectedDrive?.RootDirectory ?? dialogVm.SelectedDrive?.Name ?? string.Empty;

            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceClassifier.FromDrive(dialogVm.SelectedDrive));
            var scanOptions = new MediaScanOptions(
                DrivePathOrName: selectedDrivePath,
                MediaName: string.IsNullOrWhiteSpace(dialogVm.MediaName) ? dialogVm.SelectedDrive?.DisplayName : dialogVm.MediaName,
                TargetParentId: targetParentId,
                RootType: rootType,
                IncludeMediaInfo: dialogVm.IncludeMediaInfo,
                IncludeExtendedInfo: scanErrorOptions.IncludeExtendedInfo,
                IncludeSubfolders: dialogVm.IncludeSubfolders,
                IncludeFiles: dialogVm.IncludeFiles,
                Traversal: scanTraversal,
                LocationId: dialogVm.SelectedLocationId);

            var scanner = new MediaCatalogScanner(mediaDriveProvider,
                mediaTargets: ServiceProvider.Resolve<IMediaTargetMetadataProvider>());
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Media
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);
            TrackAddingDialog(progressVm);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 60);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 80);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                progressVm.ReportError($"Failed to scan/add media: {ex.Message}", ex);
                vm.StatusText = ex.Message;
            }
            finally
            {
                FinishAddingDialog(progressVm, progressDialog);
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, selectedDrivePath);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }
            return;
        }

        if (dialogVm.SourceMode == AddToListSourceMode.Folder)
        {
            var rootType = dialogVm.TargetPlacement == AddToListTargetPlacement.NewMedia
                ? CatalogDocumentType.Media
                : CatalogDocumentType.Folder;

            var scanErrorOptions = LoadAppOptions();
            var scanTraversal = BuildScanTraversalOptions(ScanSourceClassifier.FromPath(dialogVm.SourceValue));
            var scanOptions = new FolderScanOptions(
                FolderPath: dialogVm.SourceValue,
                MediaName: string.IsNullOrWhiteSpace(dialogVm.MediaName) ? null : dialogVm.MediaName,
                TargetParentId: targetParentId,
                RootType: rootType,
                IncludeSubfolders: dialogVm.IncludeSubfolders,
                IncludeFiles: dialogVm.IncludeFiles,
                IncludeExtendedInfo: scanErrorOptions.IncludeExtendedInfo,
                Traversal: scanTraversal);

            var scanner = new FolderCatalogScanner(mediaTargets: ServiceProvider.Resolve<IMediaTargetMetadataProvider>());
            var progressVm = new AddingProgressDialogViewModel
            {
                CanCancel = true,
                SourceKind = AddingProgressSourceKind.Folder
            };
            var progressDialog = new AddingProgressWindow
            {
                DataContext = progressVm
            };
            progressDialog.Show(this);
            TrackAddingDialog(progressVm);

            IReadOnlyList<ScanIssue> scanIssues = [];
            var scanCompleted = false;
            try
            {
                var scanResult = await Task.Run(() => scanner.ScanDetailed(scanOptions, progressVm, progressVm.CancellationToken));
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                progressVm.ReportScanStatistics(scanResult.Statistics);
                scanIssues = progressVm.GetScanIssues();
                if (scanResult.Statistics.Failed > 0 && !scanErrorOptions.ShowScanErrorsAfterScan)
                {
                    vm.StatusText = LocalizationService.Current.Format(TextKeys.ScanFailedItems, scanResult.Statistics.Failed);
                    progressVm.ReportError(vm.StatusText);
                    return;
                }
                var scannedDocuments = scanResult.Documents;

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), stagePercentage: 100, overallPercentage: 60);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications));
                await Task.Delay(50, progressVm.CancellationToken);

                progressVm.ReportStage("Importing documents...", stagePercentage: null, overallPercentage: 80);
                progressVm.ReportInfo($"Importing {scannedDocuments.Count} catalog documents into library.");
                progressVm.CancellationToken.ThrowIfCancellationRequested();
                await vm.AddImportedDocumentsAsync(scannedDocuments, progressVm.CancellationToken, progressVm,
                    scanTraversal);
                progressVm.CanCancel = false;

                if (scanErrorOptions.SaveScanErrorsToComments)
                {
                    vm.ApplyScanIssuesToComments(progressVm.GetScanIssues());
                }

                progressVm.ReportStage(LocalizationService.Current.Get(TextKeys.UpdatingIndexes), stagePercentage: 100, overallPercentage: 100);
                progressVm.ReportInfo(LocalizationService.Current.Get(TextKeys.UpdatingIndexes));
                await Task.Delay(50);

                vm.StatusText = LocalizationService.Current.Get(TextKeys.Done);
                scanCompleted = true;
            }
            catch (OperationCanceledException)
            {
                vm.StatusText = LocalizationService.Current.Get(TextKeys.ScanCanceled);
                progressVm.ReportInfo(vm.StatusText);
            }
            catch (Exception ex)
            {
                progressVm.ReportError($"Failed to scan/add folder: {ex.Message}", ex);
                vm.StatusText = ex.Message;
            }
            finally
            {
                FinishAddingDialog(progressVm, progressDialog);
            }

            if (scanCompleted)
            {
                ApplyScanCompletionEffects(scanErrorOptions, mediaSource: null);
            }

            if (scanErrorOptions.ShowScanErrorsAfterScan && scanIssues.Count > 0)
            {
                await new ScanIssuesWindow(scanIssues).ShowDialog(this);
            }
            return;
        }

        await ShowAddProgressAsync(dialogVm);

        vm.AddImportedItem(ResolveImportedName(dialogVm));
    }

    private async Task ShowAddProgressAsync(AddToListDialogViewModel addDialog)
    {
        var progressVm = new AddingProgressDialogViewModel
        {
            SourceKind = addDialog.SourceMode switch
            {
                AddToListSourceMode.Media => AddingProgressSourceKind.Media,
                AddToListSourceMode.Folder => AddingProgressSourceKind.Folder,
                AddToListSourceMode.Internet => AddingProgressSourceKind.Internet,
                _ => AddingProgressSourceKind.Generic
            }
        };
        var progressDialog = new AddingProgressWindow
        {
            DataContext = progressVm
        };

        progressDialog.Show(this);
        TrackAddingDialog(progressVm);
        try
        {
            var steps = BuildAddProgressSteps(addDialog);
            for (int i = 0; i < steps.Count; i++)
            {
                var (text, value) = steps[i];
                var stagePercent = (double)(i + 1) / steps.Count * 100;
                progressVm.ReportStage(text, stagePercentage: stagePercent, overallPercentage: value);
                progressVm.ReportInfo(text);
                await Task.Delay(140);
            }
        }
        finally
        {
            FinishAddingDialog(progressVm, progressDialog);
        }
    }

    private static IReadOnlyList<(string Text, int Value)> BuildAddProgressSteps(AddToListDialogViewModel addDialog)
    {
        return addDialog.SourceMode switch
        {
            AddToListSourceMode.Internet =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingDirectoryFromRemoteServer), 20),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 55),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ],
            AddToListSourceMode.Folder =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingSourceFolder), 25),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 60),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ],
            _ =>
            [
                (LocalizationService.Current.Get(TextKeys.ReadingMediaMetadata), 35),
                (LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications), 70),
                (LocalizationService.Current.Get(TextKeys.UpdatingIndexes), 100)
            ]
        };
    }

    private ScanTraversalOptions BuildScanTraversalOptions(ScanSourceProfile? sourceProfile = null)
    {
        return ScanSettingsApplicator.BuildTraversalOptions(
            LoadAppOptions(),
            scanExclusionRuleRepository.GetOrdered(),
            sourceProfile);
    }

    /// <summary>
    /// Runs the completion actions the user enabled (eject, activate, sound) after a scan and its import
    /// finished successfully. Failures are logged inside the service and never disturb the finished scan.
    /// </summary>
    private void ApplyScanCompletionEffects(AppOptionsDocument options, string? mediaSource)
    {
        var settings = ScanCompletionSettings.FromOptions(options);
        if (!settings.HasAnyAction)
        {
            return;
        }

        scanCompletionService.Complete(settings, mediaSource);
    }

    private static string? ResolveImportedName(AddToListDialogViewModel dialogVm)
    {
        if (!string.IsNullOrWhiteSpace(dialogVm.MediaName))
        {
            return dialogVm.MediaName;
        }

        if (!string.IsNullOrWhiteSpace(dialogVm.SourceValue))
        {
            var value = dialogVm.SourceValue.Trim();
            return Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }
}
