using System;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject, IOperationProgress
{
    private readonly object gate = new();
    private string? currentStage;

    [ObservableProperty]
    private string operationText = LocalizationService.Current.Get(TextKeys.PreparingDatabaseForModifications);

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private double overallProgressValue;

    [ObservableProperty]
    private bool isOverallIndeterminate = true;

    [ObservableProperty]
    private string overallProgressText = string.Empty;

    [ObservableProperty]
    private string currentStageName = string.Empty;

    [ObservableProperty]
    private string currentStageText = string.Empty;

    [ObservableProperty]
    private double currentStageProgressValue;

    [ObservableProperty]
    private bool isStageIndeterminate = true;

    [ObservableProperty]
    private string currentStageProgressText = string.Empty;

    [ObservableProperty]
    private string currentItemText = string.Empty;

    [ObservableProperty]
    private int maxLogEntries = 1000;

    [ObservableProperty]
    private int totalLoggedCount;

    [ObservableProperty]
    private int processedCount;

    [ObservableProperty]
    private int skippedCount;

    [ObservableProperty]
    private int failedCount;

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private int warningCount;

    [ObservableProperty]
    private bool hasErrors;

    [ObservableProperty]
    private bool hasSkipped;

    [ObservableProperty]
    private bool hasFailed;

    [ObservableProperty]
    private string summaryText = string.Empty;

    [ObservableProperty]
    private int selectedTabIndex;

    public ObservableCollection<OperationLogEntry> LogEntries { get; } = new();

    public AddingProgressDialogViewModel()
    {
        UpdateSummaryText();
    }

    partial void OnProgressValueChanged(int value)
    {
        if (Math.Abs(overallProgressValue - value) > 0.001)
        {
            overallProgressValue = value;
            isOverallIndeterminate = false;
            overallProgressText = $"{value}%";
            OnPropertyChanged(nameof(OverallProgressValue));
            OnPropertyChanged(nameof(IsOverallIndeterminate));
            OnPropertyChanged(nameof(OverallProgressText));
        }
    }

    [RelayCommand]
    public void ClearLog()
    {
        RunOnUi(() => LogEntries.Clear());
    }

    public string GetFullLogText()
    {
        lock (gate)
        {
            var sb = new StringBuilder();
            foreach (var entry in LogEntries)
            {
                sb.AppendLine(entry.DisplayText);
            }
            return sb.ToString();
        }
    }

    public void Report(OperationProgressReport report)
    {
        if (report is null)
        {
            return;
        }

        RunOnUi(() => ApplyReport(report));
    }

    public void Report((string Status, int Percentage) value)
    {
        RunOnUi(() =>
        {
            OperationText = value.Status;
            ProgressValue = value.Percentage;
            CurrentStageText = value.Status;
            CurrentStageProgressValue = value.Percentage;
            IsStageIndeterminate = false;
            AddLogEntryInternal(new OperationLogEntry(
                OperationLogLevel.Information,
                value.Status,
                details: $"{value.Percentage}%"));
        });
    }

    public void ReportStage(
        string stageName,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null)
    {
        currentStage = stageName;
        Report(OperationProgressReport.CreateStage(
            stageName,
            stagePercentage,
            overallPercentage,
            stageCurrent,
            stageTotal,
            overallCurrent,
            overallTotal));
    }

    public void ReportItem(
        string currentItem,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null)
    {
        Report(OperationProgressReport.CreateItem(
            currentItem,
            stagePercentage,
            overallPercentage,
            stageCurrent,
            stageTotal,
            overallCurrent,
            overallTotal));
    }

    public void ReportOverall(
        double? overallPercentage,
        long? overallCurrent = null,
        long? overallTotal = null,
        string? operationTitle = null)
    {
        Report(new OperationProgressReport
        {
            OperationTitle = operationTitle,
            OverallProgressPercentage = overallPercentage,
            IsOverallIndeterminate = overallPercentage.HasValue ? false : true,
            OverallWorkCurrent = overallCurrent,
            OverallWorkTotal = overallTotal
        });
    }

    public void ReportLog(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null)
    {
        var entry = new OperationLogEntry(level, message, stage ?? currentStage, itemPath, details);
        Report(OperationProgressReport.CreateLog(entry));
    }

    public void ReportInfo(string message, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Information, message, stage, itemPath);

    public void ReportWarning(string message, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Warning, message, stage, itemPath);

    public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Error, message, stage, itemPath, exception?.ToString());

    public void ReportSkipped(string itemPath, string reason, string? stage = null) =>
        ReportLog(OperationLogLevel.ItemSkipped, $"Skipped: {reason}", stage, itemPath, reason);

    public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) =>
        ReportLog(
            OperationLogLevel.ItemFailed,
            $"Failed: {reason}",
            stage,
            itemPath,
            exception?.ToString() ?? reason);

    private void ApplyReport(OperationProgressReport report)
    {
        if (!string.IsNullOrEmpty(report.OperationTitle))
        {
            OperationText = report.OperationTitle;
        }

        if (!string.IsNullOrEmpty(report.StageName))
        {
            CurrentStageName = report.StageName;
            CurrentStageText = report.StageName;
        }

        if (report.CurrentItem is not null)
        {
            CurrentItemText = report.CurrentItem;
        }

        if (report.OverallProgressPercentage.HasValue)
        {
            OverallProgressValue = Math.Clamp(report.OverallProgressPercentage.Value, 0, 100);
            var newIntProgress = (int)Math.Round(OverallProgressValue);
            if (ProgressValue != newIntProgress)
            {
                ProgressValue = newIntProgress;
            }
            IsOverallIndeterminate = false;

            if (report.OverallWorkTotal.HasValue && report.OverallWorkTotal.Value > 0)
            {
                OverallProgressText = $"{report.OverallWorkCurrent ?? 0} / {report.OverallWorkTotal.Value} ({(int)OverallProgressValue}%)";
            }
            else
            {
                OverallProgressText = $"{(int)OverallProgressValue}%";
            }
        }
        else if (report.IsOverallIndeterminate.HasValue)
        {
            IsOverallIndeterminate = report.IsOverallIndeterminate.Value;
            if (IsOverallIndeterminate)
            {
                OverallProgressText = string.Empty;
            }
        }

        if (report.StageProgressPercentage.HasValue)
        {
            CurrentStageProgressValue = Math.Clamp(report.StageProgressPercentage.Value, 0, 100);
            IsStageIndeterminate = false;

            if (report.StageWorkTotal.HasValue && report.StageWorkTotal.Value > 0)
            {
                CurrentStageProgressText = $"{report.StageWorkCurrent ?? 0} / {report.StageWorkTotal.Value} ({(int)CurrentStageProgressValue}%)";
            }
            else
            {
                CurrentStageProgressText = $"{(int)CurrentStageProgressValue}%";
            }
        }
        else if (report.IsStageIndeterminate.HasValue)
        {
            IsStageIndeterminate = report.IsStageIndeterminate.Value;
            if (IsStageIndeterminate)
            {
                CurrentStageProgressText = string.Empty;
            }
        }

        if (report.LogEntry is not null)
        {
            AddLogEntryInternal(report.LogEntry);
        }

        if (report.LogEntries is not null)
        {
            foreach (var entry in report.LogEntries)
            {
                AddLogEntryInternal(entry);
            }
        }
    }

    private void AddLogEntryInternal(OperationLogEntry entry)
    {
        lock (gate)
        {
            TotalLoggedCount++;

            switch (entry.Level)
            {
                case OperationLogLevel.ItemProcessed:
                    ProcessedCount++;
                    break;
                case OperationLogLevel.ItemSkipped:
                    SkippedCount++;
                    HasSkipped = true;
                    break;
                case OperationLogLevel.ItemFailed:
                    FailedCount++;
                    HasFailed = true;
                    ErrorCount++;
                    HasErrors = true;
                    break;
                case OperationLogLevel.Error:
                    ErrorCount++;
                    HasErrors = true;
                    break;
                case OperationLogLevel.Warning:
                    WarningCount++;
                    break;
            }

            while (LogEntries.Count >= MaxLogEntries && LogEntries.Count > 0)
            {
                LogEntries.RemoveAt(0);
            }

            LogEntries.Add(entry);
            UpdateSummaryText();
        }
    }

    private void UpdateSummaryText()
    {
        SummaryText = $"Processed: {ProcessedCount} | Skipped: {SkippedCount} | Failed: {FailedCount} | Errors: {ErrorCount}";
    }

    private static void RunOnUi(Action action)
    {
        try
        {
            if (Application.Current is null)
            {
                action();
                return;
            }

            var dispatcher = Dispatcher.UIThread;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Post(action);
            }
        }
        catch
        {
            action();
        }
    }
}
