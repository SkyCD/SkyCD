using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Explicit selected-item batch generation. Nothing runs until the user starts the pass; that pass is
/// bounded, cancellable, skips unchanged entries and reports per-item outcomes including failures.
/// </summary>
public sealed partial class AiDescriptionBatchDialogViewModel : ObservableObject
{
    private readonly AiDescriptionService service;
    private readonly Action? completed;
    private CancellationTokenSource? cancellation;

    public AiDescriptionBatchDialogViewModel(
        AiDescriptionService service,
        IReadOnlyList<CatalogDocument> entries,
        Action? completed = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        this.completed = completed;
        var provider = service.GetProviderInfo();
        ProviderText = !provider.IsPluginAvailable
            ? LocalizationService.Current.Get(UiKeys.AiDescriptionUnavailable)
            : !provider.IsEnabled
                ? LocalizationService.Current.Format(TextKeys.AiDescriptionDisabled,
                    provider.UnavailableReason ?? LocalizationService.Current.Get(TextKeys.Unknown))
                : LocalizationService.Current.Format(TextKeys.AiDescriptionProvider,
                    provider.ProviderName,
                    provider.ProviderVersion,
                    provider.ProcessingDestination);
        ContextText = provider.ContextOptions is null
            ? string.Empty
            : LocalizationService.Current.Format(TextKeys.AiDescriptionContext,
                DescribeContext(provider.ContextOptions));
        ItemCountText = LocalizationService.Current.Format(TextKeys.AiDescriptionBatchItemCount, Entries.Count);
        LocalizationObserver.Observe(this, static vm => vm.RefreshText());
    }

    public IReadOnlyList<CatalogDocument> Entries { get; }

    public ObservableCollection<AiDescriptionBatchItemResult> Results { get; } = [];

    public string ProviderText { get; private set; }

    public string ContextText { get; private set; }

    public string ItemCountText { get; private set; }

    [ObservableProperty] private bool regenerateExisting;

    [ObservableProperty] private bool isRunning;

    [ObservableProperty] private double progressValue;

    [ObservableProperty] private string statusText = string.Empty;

    [ObservableProperty] private string summaryText = string.Empty;

    public bool CanStart => !IsRunning;

    public bool CanCancel => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (IsRunning || Entries.Count == 0)
        {
            return;
        }

        using var source = new CancellationTokenSource();
        cancellation = source;
        Results.Clear();
        SummaryText = string.Empty;
        ProgressValue = 0;
        IsRunning = true;
        var total = Entries.Count;
        var completedItems = 0;
        try
        {
            var progress = new Progress<AiDescriptionBatchProgress>(item =>
            {
                var finished = Interlocked.Increment(ref completedItems);
                ProgressValue = total == 0 ? 0 : finished * 100.0 / total;
                StatusText = LocalizationService.Current.Format(
                    TextKeys.AiDescriptionBatchProgress, finished, total, item.Item.Name);
            });
            var result = await service.GenerateBatchAsync(
                Entries.Select(entry => entry.Id).ToArray(),
                RegenerateExisting,
                progress,
                source.Token).ConfigureAwait(true);

            foreach (var item in result.Items)
            {
                Results.Add(item);
            }

            ProgressValue = 100;
            SummaryText = LocalizationService.Current.Format(
                TextKeys.AiDescriptionBatchSummary,
                result.GeneratedCount,
                result.SkippedCount,
                result.FailedCount,
                result.CancelledCount);
            StatusText = result.CancelledCount > 0
                ? LocalizationService.Current.Get(TextKeys.AiDescriptionBatchCancelled)
                : LocalizationService.Current.Get(TextKeys.Done);
            if (result.GeneratedCount > 0)
            {
                completed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Current.Get(TextKeys.AiDescriptionBatchCancelled);
        }
        catch (Exception exception)
        {
            StatusText = LocalizationService.Current.Format(TextKeys.AiDescriptionBatchFailed, exception.Message);
        }
        finally
        {
            cancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => cancellation?.Cancel();

    private void RefreshText()
    {
        var provider = service.GetProviderInfo();
        ProviderText = provider.IsPluginAvailable
            ? provider.IsEnabled
                ? LocalizationService.Current.Format(TextKeys.AiDescriptionProvider,
                    provider.ProviderName, provider.ProviderVersion, provider.ProcessingDestination)
                : LocalizationService.Current.Format(TextKeys.AiDescriptionDisabled,
                    provider.UnavailableReason ?? LocalizationService.Current.Get(TextKeys.Unknown))
            : LocalizationService.Current.Get(UiKeys.AiDescriptionUnavailable);
        OnPropertyChanged(nameof(ProviderText));
        OnPropertyChanged(nameof(ItemCountText));
    }

    private static string DescribeContext(
        SkyCD.Plugin.Abstractions.Capabilities.Media.DescriptionContextOptions options)
    {
        var parts = new List<string>();
        if (options.Filename) parts.Add(LocalizationService.Current.Get(UiKeys.ContextFilename));
        if (options.Metadata) parts.Add(LocalizationService.Current.Get(UiKeys.ContextMetadata));
        if (options.Text) parts.Add(LocalizationService.Current.Get(UiKeys.ContextText));
        if (options.Images) parts.Add(LocalizationService.Current.Get(UiKeys.ContextImages));
        if (options.Audio) parts.Add(LocalizationService.Current.Get(UiKeys.ContextAudio));
        return parts.Count == 0
            ? LocalizationService.Current.Get(UiKeys.ContextNone)
            : string.Join(", ", parts);
    }
}
