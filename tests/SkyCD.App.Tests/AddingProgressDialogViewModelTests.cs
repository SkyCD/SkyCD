using System;
using System.IO;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AddingProgressDialogViewModelTests
{
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
    public void ClearLog_EmptiesVisibleLogEntries()
    {
        var vm = new AddingProgressDialogViewModel();
        vm.ReportInfo("Log entry 1");
        vm.ReportInfo("Log entry 2");

        Assert.Equal(2, vm.LogEntries.Count);

        vm.ClearLogCommand.Execute(null);

        Assert.Empty(vm.LogEntries);
    }
}
