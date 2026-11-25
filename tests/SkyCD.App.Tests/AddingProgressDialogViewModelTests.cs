using System;
using System.IO;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class AddingProgressDialogViewModelTests
{
    [Fact]
    public void CancelAction_CancelsFolderTraversalAndDisablesFurtherRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var vm = new AddingProgressDialogViewModel { CanCancel = true };
        try
        {
            var token = vm.CancellationToken;
            Assert.True(vm.CancelCommand.CanExecute(null));
            vm.CancelCommand.Execute(null);
            Assert.True(token.IsCancellationRequested);
            Assert.False(vm.CancelCommand.CanExecute(null));
            Assert.True(vm.IsCancellationPending);
            Assert.Throws<OperationCanceledException>(() => new FolderCatalogScanner().Scan(
                new FolderScanOptions(directory), vm, token));
            Assert.Equal(0, vm.ErrorCount);
            Assert.DoesNotContain("Scanning complete", vm.GetFullLogText());
        }
        finally
        {
            vm.FinishCancellation();
            Directory.Delete(directory);
        }
        Assert.False(vm.IsCancellationPending);
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.Equal("Preparing database for modifications...", vm.OperationText);
        Assert.Equal(0, vm.ProgressValue);
        Assert.Equal(0.0, vm.OverallProgressValue);
        Assert.True(vm.IsOverallIndeterminate);
        Assert.True(vm.IsStageIndeterminate);
        Assert.Empty(vm.LogEntries);
        Assert.Equal(0, vm.ProcessedCount);
        Assert.Equal(0, vm.SkippedCount);
        Assert.Equal(0, vm.ErrorCount);
        Assert.False(vm.HasErrors);
        Assert.False(vm.HasSkipped);
    }

    [Fact]
    public void OperationText_CanBeUpdated()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.OperationText = "Scanning files...";

        Assert.Equal("Scanning files...", vm.OperationText);
    }

    [Fact]
    public void ProgressValue_CanBeUpdated()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.ProgressValue = 50;

        Assert.Equal(50, vm.ProgressValue);
        Assert.Equal(50.0, vm.OverallProgressValue);
        Assert.False(vm.IsOverallIndeterminate);
        Assert.Equal("50%", vm.OverallProgressText);
    }

    [Fact]
    public void ProgressValue_SupportsIncrement()
    {
        var vm = new AddingProgressDialogViewModel();

        for (int i = 1; i <= 100; i++)
        {
            vm.ProgressValue = i;
        }

        Assert.Equal(100, vm.ProgressValue);
        Assert.Equal(100.0, vm.OverallProgressValue);
    }

    [Fact]
    public void ReportStage_UpdatesCurrentStageAndOverallProgressIndependently()
    {
        var vm = new AddingProgressDialogViewModel();

        // Stage 1: Overall at 20%, stage at 50%
        vm.ReportStage("Reading metadata...", stagePercentage: 50, overallPercentage: 20, stageCurrent: 5, stageTotal: 10, overallCurrent: 2, overallTotal: 10);

        Assert.Equal("Reading metadata...", vm.CurrentStageName);
        Assert.Equal("Reading metadata...", vm.CurrentStageText);
        Assert.Equal(50.0, vm.CurrentStageProgressValue);
        Assert.False(vm.IsStageIndeterminate);
        Assert.Equal("5 / 10 (50%)", vm.CurrentStageProgressText);

        Assert.Equal(20.0, vm.OverallProgressValue);
        Assert.Equal(20, vm.ProgressValue);
        Assert.False(vm.IsOverallIndeterminate);
        Assert.Equal("2 / 10 (20%)", vm.OverallProgressText);

        // Stage 2: Stage indeterminate, overall determinate
        vm.ReportStage("Discovering files...", stagePercentage: null, overallPercentage: 35);
        Assert.True(vm.IsStageIndeterminate);
        Assert.Equal(35.0, vm.OverallProgressValue);
        Assert.False(vm.IsOverallIndeterminate);
    }

    [Fact]
    public void ReportItem_UpdatesCurrentItemAndStageProgress()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.ReportItem("/data/media/track01.flac", stagePercentage: 10, overallPercentage: 5);

        Assert.Equal("/data/media/track01.flac", vm.CurrentItemText);
        Assert.Equal(10.0, vm.CurrentStageProgressValue);
        Assert.Equal(5.0, vm.OverallProgressValue);
    }

    [Fact]
    public void StructuredEvents_AreCapturedInLogEntriesAndCounters()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.ReportInfo("Starting scan", itemPath: "/root");
        vm.ReportLog(OperationLogLevel.ItemProcessed, "Processed track1", itemPath: "/root/track1.mp3");
        vm.ReportSkipped("/root/locked.sys", "Access Denied");
        vm.ReportWarning("Missing metadata", itemPath: "/root/track2.mp3");
        vm.ReportError("Corrupted file", new InvalidOperationException("CRC fail"), itemPath: "/root/bad.bin");

        Assert.Equal(5, vm.LogEntries.Count);
        Assert.Equal(1, vm.ProcessedCount);
        Assert.Equal(1, vm.SkippedCount);
        Assert.Equal(1, vm.WarningCount);
        Assert.Equal(1, vm.ErrorCount);
        Assert.True(vm.HasSkipped);
        Assert.True(vm.HasErrors);

        var logText = vm.GetFullLogText();
        Assert.Contains("Starting scan", logText);
        Assert.Contains("Processed track1", logText);
        Assert.Contains("Skipped: Access Denied", logText);
        Assert.Contains("Missing metadata", logText);
        Assert.Contains("Corrupted file", logText);
    }

    [Fact]
    public void ReportPluginFailure_LogsErrorWithFingerprintAndCountsIt()
    {
        var vm = new AddingProgressDialogViewModel();
        var failure = PluginFailure.Create(typeof(AddingProgressDialogViewModelTests), "media",
            new InvalidOperationException("decode failed"));

        vm.ReportPluginFailure(failure);

        Assert.Equal(1, vm.ErrorCount);
        Assert.True(vm.HasErrors);
        Assert.Contains(failure.Fingerprint, vm.GetFullLogText(), StringComparison.Ordinal);
        Assert.Contains("media", vm.GetFullLogText(), StringComparison.Ordinal);
    }

    [Fact]
    public void CloseMode_IsOffByDefault_AndEnablesDismissal()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.False(vm.IsCloseMode);
        Assert.False(vm.CanDismiss);

        vm.CanCancel = true;
        Assert.True(vm.CanDismiss);

        vm.FinishCancellation();
        Assert.False(vm.CanDismiss);

        vm.IsCloseMode = true;
        Assert.True(vm.CanDismiss);
    }

    [Fact]
    public void FlushPendingReports_PublishesQueuedEntriesSynchronously()
    {
        var vm = new AddingProgressDialogViewModel();
        vm.ReportInfo("Starting scan");

        vm.FlushPendingReports();

        Assert.Contains(vm.LogEntries, entry => entry.Message == "Starting scan");
    }

    [Fact]
    public void BoundedLogRetention_EvictsOldestEntriesWhenCapacityExceeded()
    {
        var vm = new AddingProgressDialogViewModel
        {
            MaxLogEntries = 10
        };

        for (int i = 1; i <= 25; i++)
        {
            vm.ReportLog(OperationLogLevel.ItemProcessed, $"Processed file {i}", itemPath: $"/file_{i}.txt");
        }

        Assert.Equal(10, vm.LogEntries.Count);
        Assert.Equal(25, vm.TotalLoggedCount);
        Assert.Equal(25, vm.ProcessedCount);

        // Oldest visible entry should be file 16
        Assert.Contains("Processed file 16", vm.LogEntries[0].DisplayText);
        Assert.Contains("Processed file 25", vm.LogEntries[^1].DisplayText);
    }

    [Fact]
    public void SecretSanitizer_RedactsCredentialsInLogsAndReports()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.ReportInfo("Connecting to https://admin:SuperSecretPassword123@storage.server.local/catalog");
        vm.ReportWarning("Database string server=db;User Id=root;Password=MySecretPass;");
        vm.ReportError("API failed with {\"apiKey\": \"secret-token-xyz-12345\"}");

        var logText = vm.GetFullLogText();

        Assert.DoesNotContain("SuperSecretPassword123", logText);
        Assert.Contains("admin:***@storage.server.local", logText);

        Assert.DoesNotContain("MySecretPass", logText);
        Assert.Contains("Password=***", logText);

        Assert.DoesNotContain("secret-token-xyz-12345", logText);
        Assert.Contains("apiKey\": \"***", logText);
    }

    [Fact]
    public void ScanIssues_CollectWarningsAndErrorsBeyondBoundedLogRetention()
    {
        var vm = new AddingProgressDialogViewModel
        {
            MaxLogEntries = 2
        };

        vm.ReportInfo("Starting scan", itemPath: "/root");
        vm.ReportSkipped("/root/ignored.tmp", "Filtered");
        vm.ReportWarning("Metadata unreadable", itemPath: "/root/a.jpg");
        vm.ReportError("Access denied", itemPath: "/root/b.bin");
        vm.ReportFailed("/root/c.dat", "CRC mismatch");

        var issues = vm.GetScanIssues();

        Assert.Equal(3, issues.Count);
        Assert.Equal(new ScanIssue(ScanIssueSeverity.Warning, "/root/a.jpg", "Metadata unreadable"), issues[0]);
        Assert.Equal(new ScanIssue(ScanIssueSeverity.Error, "/root/b.bin", "Access denied"), issues[1]);
        Assert.Equal(new ScanIssue(ScanIssueSeverity.Error, "/root/c.dat", "Failed: CRC mismatch"), issues[2]);
        Assert.Equal(2, vm.LogEntries.Count);
    }

    [Fact]
    public void ClearLog_EmptiesVisibleLogEntries()
    {
        var vm = new AddingProgressDialogViewModel();
        vm.ReportInfo("Log entry 1");
        vm.ReportInfo("Log entry 2");

        Assert.Equal(2, vm.LogEntries.Count);

        vm.ClearLogCommand.Execute(null);

        Assert.Empty(vm.LogEntries);
    }

    [Fact]
    public void AnalysisTime_StartsOnFirstReportAndStopsOnFinish()
    {
        var clock = new ManualTimeProvider();
        var vm = new AddingProgressDialogViewModel(clock) { CanCancel = true };
        Assert.False(vm.IsAnalysisRunning);
        Assert.Equal("00:00:00", vm.AnalysisTimeText);

        vm.ReportStage("Reading source...");
        Assert.True(vm.IsAnalysisRunning);

        clock.Advance(TimeSpan.FromSeconds(20));
        vm.FinishCancellation();

        Assert.False(vm.IsAnalysisRunning);
        Assert.Equal("00:00:20", vm.AnalysisTimeText);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("00:00:20", vm.AnalysisTimeText);
    }

    [Fact]
    public void AnalysisTime_FormatsHoursMinutesAndSeconds()
    {
        var clock = new ManualTimeProvider();
        var vm = new AddingProgressDialogViewModel(clock);

        vm.StartAnalysis();
        vm.StartAnalysis();
        clock.Advance(new TimeSpan(1, 2, 3));
        vm.StopAnalysis();

        Assert.Equal("01:02:03", vm.AnalysisTimeText);
    }

    [Fact]
    public void ReportScanStatistics_UpdatesScanBlockCounts()
    {
        var vm = new AddingProgressDialogViewModel();
        var statistics = new ScanTraversalStatistics();
        for (var i = 0; i < 3; i++)
        {
            statistics.IncrementDiscovered();
        }

        statistics.IncrementSkipped();
        statistics.IncrementFailed();

        vm.ReportScanStatistics(statistics);

        Assert.Equal(3, vm.ScanDiscoveredCount);
        Assert.Equal(1, vm.ScanSkippedCount);
        Assert.Equal(1, vm.ScanFailedCount);
    }

    [Fact]
    public void FilteringText_FallsBackFromItemToStageToOperation()
    {
        var vm = new AddingProgressDialogViewModel();
        Assert.Equal(vm.OperationText, vm.FilteringText);

        vm.ReportStage("Reading source folder...");
        Assert.Equal("Reading source folder...", vm.FilteringText);

        vm.ReportItem("/source/track01.flac");
        Assert.Equal("/source/track01.flac", vm.FilteringText);
    }

    [Fact]
    public void SourceKind_ReportsTheActiveSourceForTheInlineBadge()
    {
        var vm = new AddingProgressDialogViewModel { SourceKind = AddingProgressSourceKind.Internet };

        Assert.Equal(LocalizationService.Current.Get(UiKeys.FromInternet), vm.SourceLabel);
        Assert.True(vm.IsInternetSource);
        Assert.False(vm.IsMediaSource);
        Assert.False(vm.IsFolderSource);
    }

    [Fact]
    public void GenericSourceKind_ShowsNoSourceBadge()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.Equal(string.Empty, vm.SourceLabel);
        Assert.False(vm.IsMediaSource);
        Assert.False(vm.IsFolderSource);
        Assert.False(vm.IsInternetSource);
    }

    [Fact]
    public void MergedReports_KeepEarlierFieldsTheLaterReportOmits()
    {
        var item = OperationProgressReport.CreateItem(
            "/source/track01.flac",
            stagePercentage: 10,
            overallPercentage: 5);
        var stage = OperationProgressReport.CreateStage(
            "Importing",
            stagePercentage: 50,
            overallPercentage: 20);

        var merged = AddingProgressDialogViewModel.MergePendingReport(item, stage);

        Assert.NotNull(merged);
        Assert.Equal("Importing", merged.StageName);
        Assert.Equal("/source/track01.flac", merged.CurrentItem);
        Assert.Equal(20.0, merged.OverallProgressPercentage);
        Assert.Equal(50.0, merged.StageProgressPercentage);
    }

    [Fact]
    public void HighVolumeReports_KeepCountersAndBoundedLogConsistent()
    {
        var vm = new AddingProgressDialogViewModel { MaxLogEntries = 500 };

        for (var i = 0; i < 5000; i++)
        {
            vm.ReportLog(OperationLogLevel.ItemProcessed, $"Processed file {i}", itemPath: $"/file_{i}.txt");
        }

        // Coalescing must not lose the statistics even when only the newest entries stay visible.
        Assert.Equal(5000, vm.TotalLoggedCount);
        Assert.Equal(5000, vm.ProcessedCount);
        Assert.Equal(500, vm.LogEntries.Count);
        Assert.Contains("Processed file 4999", vm.LogEntries[^1].DisplayText);
    }

    [Fact]
    public void ScanIssues_SurviveHighVolumeLogging()
    {
        var vm = new AddingProgressDialogViewModel { MaxLogEntries = 100 };

        for (var i = 0; i < 1000; i++)
        {
            vm.ReportLog(OperationLogLevel.ItemProcessed, $"Processed file {i}");
            if (i % 100 == 0)
            {
                vm.ReportError("Unreadable metadata", itemPath: $"/bad_{i}.bin");
            }
        }

        Assert.Equal(10, vm.GetScanIssues().Count);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
