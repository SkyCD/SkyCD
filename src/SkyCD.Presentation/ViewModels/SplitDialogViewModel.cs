using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Catalog.Split;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Collects the split limits, runs the split off the UI thread and publishes per-part progress, so the dialog
/// stays responsive and a cancelled run leaves no part behind. The host view supplies the runner, so the
/// view model never touches files or format plugins and stays unit-testable.
/// </summary>
public sealed partial class SplitDialogViewModel : ObservableObject
{
    private readonly Func<SplitRequest, IProgress<SplitProgress>, CancellationToken, Task<SplitResult>> runSplitAsync;
    private CancellationTokenSource? runCancellation;

    public SplitDialogViewModel(
        string sourceName,
        int entryCount,
        string initialOutputFolder,
        IReadOnlyList<SplitFormatOption> formats,
        string defaultFormatId,
        Func<SplitRequest, IProgress<SplitProgress>, CancellationToken, Task<SplitResult>> runSplitAsync)
    {
        SourceName = sourceName;
        EntryCount = entryCount;
        this.runSplitAsync = runSplitAsync;
        Formats = formats;
        outputFolder = initialOutputFolder;
        selectedSplitFormat = SelectDefaultFormat(formats, defaultFormatId);
        fileNamePattern = DefaultPatternFor(selectedSplitFormat?.DefaultExtension);
    }

    /// <summary>Gets the display name of the source catalog the parts are cut from.</summary>
    public string SourceName { get; }

    /// <summary>Gets the source catalog entry count.</summary>
    public int EntryCount { get; }

    /// <summary>Gets the source catalog entry count as dialog text.</summary>
    public string EntrySummary => LocalizationService.Current.Format(UiKeys.SplitEntrySummary, EntryCount);

    /// <summary>Gets the saving formats the parts can be written in.</summary>
    public IReadOnlyList<SplitFormatOption> Formats { get; }

    /// <summary>Gets the units a part size limit can be entered in.</summary>
    public IReadOnlyList<string> SizeUnits { get; } = ["Bytes", "KB", "MB", "GB"];

    /// <summary>Gets the summary of a completed split, or null when nothing was produced.</summary>
    public string? OutcomeMessage { get; private set; }

    /// <summary>Raised when the parts are written and the dialog can close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string outputFolder;
    [ObservableProperty] private string fileNamePattern;
    [ObservableProperty] private SplitFormatOption? selectedSplitFormat;
    [ObservableProperty] private string entriesPerPart = "1000";
    [ObservableProperty] private string partSize = string.Empty;
    [ObservableProperty] private string partSizeUnit = "MB";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    private bool isBusy;
    [ObservableProperty] private int progressPercent;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;

    private bool CanSplit => !IsBusy && EntryCount > 0;

    [RelayCommand(CanExecute = nameof(CanSplit))]
    private async Task SplitAsync()
    {
        ErrorText = string.Empty;
        StatusText = string.Empty;
        OutcomeMessage = null;
        if (SelectedSplitFormat is not { } format)
        {
            ErrorText = LocalizationService.Current.Get(TextKeys.SplitNoWritableFormat);
            return;
        }

        if (!TryBuildRequest(format, out var request, out var failure))
        {
            ErrorText = failure;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        runCancellation = cancellation;
        IsBusy = true;
        ProgressPercent = 0;
        var progress = new Progress<SplitProgress>(Report);
        try
        {
            var result = await Task.Run(() =>
                runSplitAsync(request, progress, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            ProgressPercent = 100;
            OutcomeMessage = LocalizationService.Current.Format(UiKeys.SplitOutcome,
                SourceName, EntryCount, result.Parts.Count, request.OutputFolder);
            StatusText = OutcomeMessage;
            CloseRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Current.Get(UiKeys.SplitCancelled);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            StatusText = LocalizationService.Current.Get(UiKeys.SplitFailed);
        }
        finally
        {
            runCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => runCancellation?.Cancel();

    partial void OnSelectedSplitFormatChanged(SplitFormatOption? value)
    {
        if (value is null)
        {
            return;
        }

        FileNamePattern = WithExtension(FileNamePattern, value.DefaultExtension);
    }

    private void Report(SplitProgress progress)
    {
        ProgressPercent = progress.Percent;
        StatusText = LocalizationService.Current.Format(UiKeys.SplitProgressStatus,
            progress.PartsWritten, progress.TotalParts, progress.EntriesWritten, progress.TotalEntries);
    }

    private bool TryBuildRequest(SplitFormatOption format, out SplitRequest request, out string failure)
    {
        request = null!;
        if (!TryLimits(out var entries, out var bytes, out failure))
        {
            return false;
        }

        var folder = OutputFolder.Trim();
        if (folder.Length == 0)
        {
            failure = LocalizationService.Current.Get(UiKeys.SplitOutputFolderRequired);
            return false;
        }

        string pattern;
        try
        {
            pattern = SplitPattern.Normalize(FileNamePattern);
        }
        catch (SplitPatternException exception)
        {
            failure = exception.Message;
            return false;
        }

        if (!ExtensionMatchesFormat(pattern, format))
        {
            failure = LocalizationService.Current.Format(UiKeys.SplitPatternExtensionMismatch,
                Path.GetExtension(pattern), format.DisplayName, string.Join(", ", format.Extensions));
            return false;
        }

        request = new SplitRequest
        {
            SourceName = SourceName,
            OutputFolder = folder,
            FileNamePattern = pattern,
            FormatId = format.FormatId,
            MaxEntriesPerPart = entries,
            MaxPartSizeBytes = bytes
        };
        failure = string.Empty;
        return true;
    }

    private bool TryLimits(out int entries, out long bytes, out string failure)
    {
        entries = 0;
        bytes = 0;
        var entryText = EntriesPerPart.Trim();
        if (entryText.Length == 0
            || !int.TryParse(entryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out entries)
            || entries <= 0)
        {
            failure = entryText.Length == 0
                ? LocalizationService.Current.Get(UiKeys.SplitEntriesRequired)
                : LocalizationService.Current.Format(UiKeys.SplitEntriesInvalid, entryText);
            return false;
        }

        var sizeText = PartSize.Trim();
        if (sizeText.Length == 0)
        {
            failure = string.Empty;
            return true;
        }

        if (!double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size <= 0)
        {
            failure = LocalizationService.Current.Format(UiKeys.SplitSizeInvalid, sizeText);
            return false;
        }

        var multiplier = PartSizeUnit switch
        {
            "Bytes" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            _ => 0L
        };
        if (multiplier == 0)
        {
            failure = LocalizationService.Current.Get(UiKeys.SplitSizeUnitRequired);
            return false;
        }

        try
        {
            bytes = checked((long)(size * multiplier));
        }
        catch (OverflowException)
        {
            failure = LocalizationService.Current.Get(UiKeys.SplitSizeTooLarge);
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static SplitFormatOption? SelectDefaultFormat(
        IReadOnlyList<SplitFormatOption> formats, string defaultFormatId)
    {
        foreach (var format in formats)
        {
            if (format.FormatId.Equals(defaultFormatId, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return formats.Count > 0 ? formats[0] : null;
    }

    private static string DefaultPatternFor(string? defaultExtension) =>
        string.IsNullOrWhiteSpace(defaultExtension)
            ? SplitPattern.Default
            : $"part #N.{defaultExtension.TrimStart('.')}";

    private static string WithExtension(string pattern, string defaultExtension)
    {
        var stem = pattern.Trim();
        var extension = Path.GetExtension(stem);
        if (stem.Length == 0 || extension.Length == 0)
        {
            return DefaultPatternFor(defaultExtension);
        }

        return stem[..^extension.Length] + "." + defaultExtension.TrimStart('.');
    }

    private static bool ExtensionMatchesFormat(string pattern, SplitFormatOption format)
    {
        var extension = Path.GetExtension(pattern);
        foreach (var candidate in format.Extensions)
        {
            var normalized = candidate.StartsWith('.') ? candidate : "." + candidate;
            if (extension.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
