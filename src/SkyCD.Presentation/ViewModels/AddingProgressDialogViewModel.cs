using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.SDK;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class AddingProgressDialogViewModel : ObservableObject, IOperationProgress, IThumbnailProgress
{
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly IPreviewImageDecoder thumbnailDecoder;
    private CancellationTokenSource? thumbnailCancellation;
    private int thumbnailGeneration;
    private bool cancellationFinished;
    private readonly TimeProvider timeProvider;
    private DateTimeOffset? analysisStartedAt;
    private DispatcherTimer? analysisTicker;
    private TimeSpan analysisElapsed;

    public bool IsCancellationPending => !cancellationFinished && cancellationSource.IsCancellationRequested;

    public CancellationToken CancellationToken => cancellationSource.Token;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    private bool canCancel;

    /// <summary>
    /// Set when the operation ends with errors: the dialog stays open and the footer button
    /// becomes Close instead of Cancel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    private bool isCloseMode;

    /// <summary>The footer button dismisses the dialog while it can cancel, or closes it afterwards.</summary>
    public bool CanDismiss => CanCancel || IsCloseMode;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (!CanCancel) return;
        CanCancel = false;
        cancellationSource.Cancel();
        ReportInfo(LocalizationService.Current.Get(TextKeys.ScanCanceled));
    }

    public void FinishCancellation()
    {
        CanCancel = false;
        cancellationFinished = true;
        isCompleted = true;
        FlushPending();
        StopAnalysis();
        CancelThumbnailDecode();
        cancellationSource.Dispose();
    }

    private readonly object gate = new();
    private string? currentStage;
    private string? scanRootPath;

    // Scan reports arrive from a worker thread faster than the UI can lay them out (#1063).
    // They are merged into this pending state and published on the UI thread at a bounded rate.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);
    private const int MaxEntriesPerFlush = 250;

    private OperationProgressReport? pendingReport;
    private readonly Queue<OperationLogEntry> pendingEntries = new();
    private bool flushScheduled;
    private bool isCompleted;
    private DateTimeOffset lastFlushAt = DateTimeOffset.MinValue;
    private int loggedTotal;
    private int processedTotal;
    private int skippedTotal;
    private int failedTotal;
    private int errorTotal;
    private int warningTotal;
    private bool skippedSeen;
    private bool failedSeen;
    private bool errorsSeen;

    [ObservableProperty]
    private AddingProgressSourceKind sourceKind;

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
    private long scanDiscoveredCount;

    [ObservableProperty]
    private long scanSkippedCount;

    [ObservableProperty]
    private long scanFailedCount;

    public ObservableCollection<OperationLogEntry> LogEntries { get; } = new();

    /// <summary>The most recently produced thumbnail of the running operation, once it has been decoded.</summary>
    [ObservableProperty]
    private IImage? latestThumbnail;

    public bool HasLatestThumbnail => LatestThumbnail is not null;

    public string FilteringText => !string.IsNullOrEmpty(CurrentItemText)
        ? CurrentItemText
        : !string.IsNullOrEmpty(CurrentStageText)
            ? CurrentStageText
            : OperationText;

    public string StageCaptionText => string.IsNullOrEmpty(CurrentStageText) ? OperationText : CurrentStageText;

    /// <summary>Localized name of the active add-source, shown inline on the filtering line.</summary>
    public string SourceLabel => SourceKind switch
    {
        AddingProgressSourceKind.Media => LocalizationService.Current.Get(UiKeys.FromMedia),
        AddingProgressSourceKind.Folder => LocalizationService.Current.Get(UiKeys.FromFolder),
        AddingProgressSourceKind.Internet => LocalizationService.Current.Get(UiKeys.FromInternet),
        _ => string.Empty
    };

    public bool IsMediaSource => SourceKind == AddingProgressSourceKind.Media;

    public bool IsFolderSource => SourceKind == AddingProgressSourceKind.Folder;

    public bool IsInternetSource => SourceKind == AddingProgressSourceKind.Internet;

    public bool IsAnalysisRunning => analysisStartedAt.HasValue;

    public TimeSpan AnalysisElapsed
    {
        get => analysisElapsed;
        private set
        {
            if (analysisElapsed == value)
            {
                return;
            }

            analysisElapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AnalysisTimeText));
        }
    }

    public string AnalysisTimeText => FormatAnalysisTime(AnalysisElapsed);

    private readonly List<ScanIssue> scanIssues = new();

    /// <summary>
    /// Every warning and error reported so far, independent of the bounded log entry list.
    /// </summary>
    public IReadOnlyList<ScanIssue> GetScanIssues()
    {
        lock (gate)
        {
            return scanIssues.ToArray();
        }
    }

    public AddingProgressDialogViewModel() : this(TimeProvider.System)
    {
    }

    public AddingProgressDialogViewModel(TimeProvider timeProvider, IPreviewImageDecoder? thumbnailDecoder = null)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.thumbnailDecoder = thumbnailDecoder ?? new AvaloniaPreviewImageDecoder();
    }

    /// <summary>Starts the analysis clock once; later calls while running are ignored.</summary>
    public void StartAnalysis()
    {
        if (isCompleted || analysisStartedAt.HasValue)
        {
            return;
        }

        analysisStartedAt = timeProvider.GetUtcNow();
        AnalysisElapsed = TimeSpan.Zero;
        OnPropertyChanged(nameof(IsAnalysisRunning));
        StartAnalysisTicker();
    }

    /// <summary>Stops the analysis clock and publishes the final elapsed time.</summary>
    public void StopAnalysis()
    {
        if (!analysisStartedAt.HasValue)
        {
            return;
        }

        UpdateAnalysisElapsed();
        analysisStartedAt = null;
        analysisTicker?.Stop();
        analysisTicker = null;
        OnPropertyChanged(nameof(IsAnalysisRunning));
    }

    public void ReportScanStatistics(ScanTraversalStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        RunOnUi(() =>
        {
            ScanDiscoveredCount = statistics.Discovered;
            ScanSkippedCount = statistics.Skipped;
            ScanFailedCount = statistics.Failed;
        });
    }

    partial void OnCurrentItemTextChanged(string value) => OnPropertyChanged(nameof(FilteringText));

    partial void OnLatestThumbnailChanged(IImage? value) => OnPropertyChanged(nameof(HasLatestThumbnail));

    partial void OnCurrentStageTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteringText));
        OnPropertyChanged(nameof(StageCaptionText));
    }

    partial void OnOperationTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteringText));
        OnPropertyChanged(nameof(StageCaptionText));
    }

    partial void OnSourceKindChanged(AddingProgressSourceKind value)
    {
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(IsMediaSource));
        OnPropertyChanged(nameof(IsFolderSource));
        OnPropertyChanged(nameof(IsInternetSource));
    }

    /// <summary>
    /// Starts decoding a freshly produced thumbnail. The newest report always wins: every earlier decode is
    /// cancelled and its result discarded, even when it completes later. Decode failures are ignored, because
    /// a plugin payload the platform cannot read must never fail the running operation.
    /// </summary>
    /// <returns>The decode that publishes this thumbnail, which finishes without applying a superseded result.</returns>
    public Task ReportThumbnail(MediaThumbnail thumbnail)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        if (cancellationFinished)
        {
            return Task.CompletedTask;
        }

        var generation = Interlocked.Increment(ref thumbnailGeneration);

        CancellationTokenSource cancellation;
        try
        {
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
        }
        catch (ObjectDisposedException)
        {
            return Task.CompletedTask;
        }

        var previous = Interlocked.Exchange(ref thumbnailCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        if (cancellation.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return DecodeThumbnailAsync(generation, thumbnail.Data, thumbnail.ContentType, cancellation.Token);
    }

    private async Task DecodeThumbnailAsync(
        int generation,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken)
    {
        IImage? image;
        try
        {
            image = await thumbnailDecoder.DecodeAsync(data, contentType, data.Length, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        if (image is null || IsThumbnailSuperseded(generation, cancellationToken))
        {
            return;
        }

        RunOnUi(() =>
        {
            if (IsThumbnailSuperseded(generation, cancellationToken))
            {
                return;
            }

            LatestThumbnail = image;
        });
    }

    private bool IsThumbnailSuperseded(int generation, CancellationToken cancellationToken) =>
        Volatile.Read(ref thumbnailGeneration) != generation || cancellationToken.IsCancellationRequested;

    private void CancelThumbnailDecode()
    {
        Interlocked.Increment(ref thumbnailGeneration);
        var cancellation = Interlocked.Exchange(ref thumbnailCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void StartAnalysisTicker()
    {
        if (analysisTicker is not null || Application.Current is null)
        {
            return;
        }

        analysisTicker = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateAnalysisElapsed());
        analysisTicker.Start();
    }

    private void UpdateAnalysisElapsed()
    {
        if (analysisStartedAt is not { } startedAt)
        {
            return;
        }

        AnalysisElapsed = timeProvider.GetUtcNow() - startedAt;
    }

    private static string FormatAnalysisTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
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
        RunOnUi(() =>
        {
            lock (gate)
            {
                pendingEntries.Clear();
                LogEntries.Clear();
            }
        });
    }

    public string GetFullLogText()
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            FlushPending();
        }

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

        lock (gate)
        {
            pendingReport = MergePendingReport(pendingReport, report);
            if (report.LogEntry is { } entry)
            {
                EnqueueEntryLocked(entry);
            }

            if (report.LogEntries is { } entries)
            {
                foreach (var item in entries)
                {
                    EnqueueEntryLocked(item);
                }
            }
        }

        ScheduleFlush();
    }

    public void Report((string Status, int Percentage) value) =>
        Report(new OperationProgressReport
        {
            OperationTitle = value.Status,
            StageName = value.Status,
            OverallProgressPercentage = value.Percentage,
            IsOverallIndeterminate = false,
            StageProgressPercentage = value.Percentage,
            IsStageIndeterminate = false,
            LogEntry = new OperationLogEntry(
                OperationLogLevel.Information,
                value.Status,
                details: $"{value.Percentage}%")
        });

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

    /// <summary>
    /// Routes a plugin failure into the operation log while an add is running, so no global error
    /// dialog interrupts the operation. The full fingerprint is kept in the log for later triage.
    /// </summary>
    public void ReportPluginFailure(PluginFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var identity = failure.PluginIdentity;
        var shortIdentity = identity.Length > 8 ? identity[..8] + "…" : identity;
        ReportLog(
            OperationLogLevel.Error,
            $"Plugin error during '{failure.Operation}' (plugin {shortIdentity})",
            details: $"fingerprint {failure.Fingerprint}");
    }

    /// <summary>
    /// Publishes every queued report immediately; must run on the UI thread. Completion paths call
    /// this before reading <see cref="HasErrors"/> so a trailing error cannot slip past unseen.
    /// </summary>
    public void FlushPendingReports()
    {
        while (true)
        {
            FlushPending();

            lock (gate)
            {
                if (pendingEntries.Count == 0 && pendingReport is null)
                {
                    return;
                }
            }
        }
    }

    public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) =>
        ReportLog(
            OperationLogLevel.ItemFailed,
            $"Failed: {reason}",
            stage,
            itemPath,
            exception?.ToString() ?? reason);

    private void ApplyReport(OperationProgressReport report)
    {
        StartAnalysis();

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
    }

    internal static OperationProgressReport MergePendingReport(
        OperationProgressReport? pending,
        OperationProgressReport incoming) => new()
        {
            OperationTitle = incoming.OperationTitle ?? pending?.OperationTitle,
            StageName = incoming.StageName ?? pending?.StageName,
            CurrentItem = incoming.CurrentItem ?? pending?.CurrentItem,
            OverallProgressPercentage = incoming.OverallProgressPercentage ?? pending?.OverallProgressPercentage,
            IsOverallIndeterminate = incoming.IsOverallIndeterminate ?? pending?.IsOverallIndeterminate,
            OverallWorkCurrent = incoming.OverallWorkCurrent ?? pending?.OverallWorkCurrent,
            OverallWorkTotal = incoming.OverallWorkTotal ?? pending?.OverallWorkTotal,
            StageProgressPercentage = incoming.StageProgressPercentage ?? pending?.StageProgressPercentage,
            IsStageIndeterminate = incoming.IsStageIndeterminate ?? pending?.IsStageIndeterminate,
            StageWorkCurrent = incoming.StageWorkCurrent ?? pending?.StageWorkCurrent,
            StageWorkTotal = incoming.StageWorkTotal ?? pending?.StageWorkTotal
        };

    /// <summary>
    /// Learns the scan root from the first scan-start entry (the Information entry that carries
    /// the root as its path, e.g. "Starting folder scan") and stamps it on later entries so the
    /// compact line can render descendants relative to the root. Entries that already carry an
    /// explicit root keep it; entries outside the learned root keep absolute paths.
    /// </summary>
    private OperationLogEntry StampScanRootLocked(OperationLogEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ScanRoot))
        {
            scanRootPath = entry.ScanRoot;
            return entry;
        }

        if (string.IsNullOrEmpty(scanRootPath)
            && entry.Level == OperationLogLevel.Information
            && !string.IsNullOrEmpty(entry.ItemPath))
        {
            scanRootPath = entry.ItemPath;
        }

        return string.IsNullOrEmpty(scanRootPath) ? entry : entry with { ScanRoot = scanRootPath };
    }

    private void EnqueueEntryLocked(OperationLogEntry entry)
    {
        entry = StampScanRootLocked(entry);
        loggedTotal++;

        switch (entry.Level)
        {
            case OperationLogLevel.ItemProcessed:
                processedTotal++;
                break;
            case OperationLogLevel.ItemSkipped:
                skippedTotal++;
                skippedSeen = true;
                break;
            case OperationLogLevel.ItemFailed:
                failedTotal++;
                failedSeen = true;
                errorTotal++;
                errorsSeen = true;
                break;
            case OperationLogLevel.Error:
                errorTotal++;
                errorsSeen = true;
                break;
            case OperationLogLevel.Warning:
                warningTotal++;
                break;
        }

        if (TryCreateScanIssue(entry) is { } issue)
        {
            scanIssues.Add(issue);
        }

        if (MaxLogEntries <= 0)
        {
            return;
        }

        pendingEntries.Enqueue(entry);
        while (pendingEntries.Count > MaxLogEntries)
        {
            pendingEntries.Dequeue();
        }
    }

    private void ScheduleFlush()
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            FlushPending();
            return;
        }

        lock (gate)
        {
            if (flushScheduled)
            {
                return;
            }

            flushScheduled = true;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var remaining = lastFlushAt + FlushInterval - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    FlushPending();
                }
                else
                {
                    DispatcherTimer.RunOnce(FlushPending, remaining, DispatcherPriority.Background);
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>Publishes the merged report and one bounded batch of log entries; must run on the UI thread.</summary>
    private void FlushPending()
    {
        OperationProgressReport? report;
        List<OperationLogEntry> batch;
        int logged;
        int processed;
        int skipped;
        int failed;
        int errors;
        int warnings;
        bool hasSkipped;
        bool hasFailed;
        bool hasErrors;
        bool hasMoreEntries;

        lock (gate)
        {
            flushScheduled = false;
            lastFlushAt = timeProvider.GetUtcNow();

            report = pendingReport;
            pendingReport = null;

            batch = new List<OperationLogEntry>(Math.Min(pendingEntries.Count, MaxEntriesPerFlush));
            while (batch.Count < MaxEntriesPerFlush && pendingEntries.Count > 0)
            {
                batch.Add(pendingEntries.Dequeue());
            }

            hasMoreEntries = pendingEntries.Count > 0;

            logged = loggedTotal;
            processed = processedTotal;
            skipped = skippedTotal;
            failed = failedTotal;
            errors = errorTotal;
            warnings = warningTotal;
            hasSkipped = skippedSeen;
            hasFailed = failedSeen;
            hasErrors = errorsSeen;
        }

        if (report is not null)
        {
            ApplyReport(report);
        }

        AppendLogEntries(batch);

        if (TotalLoggedCount != logged) TotalLoggedCount = logged;
        if (ProcessedCount != processed) ProcessedCount = processed;
        if (SkippedCount != skipped) SkippedCount = skipped;
        if (FailedCount != failed) FailedCount = failed;
        if (ErrorCount != errors) ErrorCount = errors;
        if (WarningCount != warnings) WarningCount = warnings;
        if (HasSkipped != hasSkipped) HasSkipped = hasSkipped;
        if (HasFailed != hasFailed) HasFailed = hasFailed;
        if (HasErrors != hasErrors) HasErrors = hasErrors;

        if (hasMoreEntries)
        {
            ScheduleContinuationFlush();
        }
    }

    private void ScheduleContinuationFlush()
    {
        if (Application.Current is null)
        {
            FlushPending();
            return;
        }

        lock (gate)
        {
            if (flushScheduled)
            {
                return;
            }

            flushScheduled = true;
        }

        DispatcherTimer.RunOnce(FlushPending, FlushInterval, DispatcherPriority.Background);
    }

    private void AppendLogEntries(IReadOnlyList<OperationLogEntry> entries)
    {
        if (entries.Count == 0 || MaxLogEntries <= 0)
        {
            return;
        }

        var overflow = Math.Max(0, LogEntries.Count + entries.Count - MaxLogEntries);
        while (overflow-- > 0 && LogEntries.Count > 0)
        {
            LogEntries.RemoveAt(0);
        }

        foreach (var entry in entries)
        {
            LogEntries.Add(entry);
        }
    }

    private static ScanIssue? TryCreateScanIssue(OperationLogEntry entry) => entry.Level switch
    {
        OperationLogLevel.Warning => new ScanIssue(ScanIssueSeverity.Warning, entry.ItemPath, entry.Message),
        OperationLogLevel.Error => new ScanIssue(ScanIssueSeverity.Error, entry.ItemPath, entry.Message),
        OperationLogLevel.ItemFailed => new ScanIssue(ScanIssueSeverity.Error, entry.ItemPath, entry.Message),
        _ => null
    };

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
