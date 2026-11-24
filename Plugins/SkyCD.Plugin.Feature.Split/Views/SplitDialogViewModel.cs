using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Feature.Split.Export;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;

namespace SkyCD.Plugin.Feature.Split.Views;

/// <summary>
/// Collects the split limits, runs the split off the UI thread and publishes per-part progress, so the dialog
/// stays responsive and a cancelled run leaves no part behind.
/// </summary>
public sealed partial class SplitDialogViewModel : ObservableObject
{
    private readonly LegacyTextCatalog catalog;
    private CancellationTokenSource? runCancellation;

    public SplitDialogViewModel(string sourcePath, LegacyTextCatalog catalog)
    {
        SourcePath = sourcePath;
        this.catalog = catalog;
        outputFolder = Path.GetDirectoryName(sourcePath) ?? string.Empty;
    }

    /// <summary>Gets the catalog the parts are cut from.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the source catalog entry count as dialog text.</summary>
    public string EntrySummary => $"{catalog.EntryCount} entries";

    /// <summary>Gets the units a part size limit can be entered in.</summary>
    public IReadOnlyList<string> SizeUnits { get; } = ["Bytes", "KB", "MB", "GB"];

    /// <summary>Gets the summary of a completed split, or null when nothing was produced.</summary>
    public string? OutcomeMessage { get; private set; }

    /// <summary>Raised when the parts are written and the dialog can close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string outputFolder;
    [ObservableProperty] private string fileNamePattern = SplitPattern.Default;
    [ObservableProperty] private string entriesPerPart = "1000";
    [ObservableProperty] private string partSize = string.Empty;
    [ObservableProperty] private string partSizeUnit = "MB";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))]
    private bool isBusy;
    [ObservableProperty] private int progressPercent;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;

    private bool CanSplit => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSplit))]
    private async Task SplitAsync()
    {
        ErrorText = string.Empty;
        StatusText = string.Empty;
        OutcomeMessage = null;
        if (!TryBuildRequest(out var request, out var failure))
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
                LegacyScdCatalogSplitter.SplitAsync(catalog, request, progress, cancellation.Token)).ConfigureAwait(true);
            ProgressPercent = 100;
            OutcomeMessage = $"Split '{Path.GetFileName(SourcePath)}' ({catalog.EntryCount} entries) into " +
                             $"{result.Parts.Count} part file(s) in '{request.OutputFolder}'.";
            StatusText = OutcomeMessage;
            CloseRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Split cancelled. No part files were kept.";
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            StatusText = "Split failed. No part files were kept.";
        }
        finally
        {
            runCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => runCancellation?.Cancel();

    private void Report(SplitProgress progress)
    {
        ProgressPercent = progress.Percent;
        StatusText = $"Wrote part {progress.PartsWritten} of {progress.TotalParts} " +
                     $"({progress.EntriesWritten} of {progress.TotalEntries} entries).";
    }

    private bool TryBuildRequest(out SplitRequest request, out string failure)
    {
        request = null!;
        if (!TryLimits(out var entries, out var bytes, out failure))
        {
            return false;
        }

        try
        {
            request = new SplitRequest
            {
                SourcePath = SourcePath,
                OutputFolder = OutputFolder.Trim(),
                FileNamePattern = SplitPattern.Normalize(FileNamePattern),
                MaxEntriesPerPart = entries,
                MaxPartSizeBytes = bytes
            };
        }
        catch (SplitPatternException exception)
        {
            failure = exception.Message;
            return false;
        }

        if (entries == 0 && bytes == 0)
        {
            failure = "Set a maximum entry count, a maximum part size, or both.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private bool TryLimits(out int entries, out long bytes, out string failure)
    {
        entries = 0;
        bytes = 0;
        failure = string.Empty;
        var entryText = EntriesPerPart.Trim();
        if (entryText.Length > 0
            && (!int.TryParse(entryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out entries)
                || entries <= 0))
        {
            failure = $"The maximum entries per part must be a positive whole number, not '{entryText}'.";
            return false;
        }

        var sizeText = PartSize.Trim();
        if (sizeText.Length == 0)
        {
            return true;
        }

        if (!double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size <= 0)
        {
            failure = $"The part size must be a positive number, not '{sizeText}'.";
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
            failure = "Choose the unit of the part size.";
            return false;
        }

        try
        {
            bytes = checked((long)(size * multiplier));
        }
        catch (OverflowException)
        {
            failure = "The part size is too large.";
            return false;
        }

        return true;
    }
}
