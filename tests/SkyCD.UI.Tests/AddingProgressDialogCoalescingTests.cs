using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1063: a scan reports far faster than the UI can lay out. Worker-thread reports must be
/// coalesced and published once on the UI thread instead of queuing one dispatcher job per item.
/// </summary>
public sealed class AddingProgressDialogCoalescingTests
{
    [AvaloniaFact]
    public void WorkerThreadReports_AreCoalescedIntoASingleUiUpdate()
    {
        var viewModel = new AddingProgressDialogViewModel { CanCancel = true };
        var worker = new Thread(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                viewModel.ReportItem($"/source/file_{i}.bin", stagePercentage: i / 10.0, overallPercentage: i / 10.0);
            }

            viewModel.ReportLog(OperationLogLevel.ItemProcessed, "Processed item", itemPath: "/source/file_999.bin");
        });

        try
        {
            worker.Start();
            worker.Join();

            // Nothing has touched the bound properties yet: the worker only merged its reports.
            Assert.Equal(0, viewModel.ProcessedCount);
            Assert.Empty(viewModel.LogEntries);
            Assert.Equal(string.Empty, viewModel.CurrentItemText);

            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, viewModel.ProcessedCount);
            Assert.Equal(1, viewModel.TotalLoggedCount);
            Assert.Single(viewModel.LogEntries);
            Assert.Equal("/source/file_999.bin", viewModel.CurrentItemText);
            Assert.Equal(100, viewModel.ProgressValue);
        }
        finally
        {
            viewModel.FinishCancellation();
        }
    }
}
