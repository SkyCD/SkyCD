using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #907: the add-to-list progress window shows the classic Analysing Volume sections
/// together — filtering line, stat blocks, one progress bar, an always-visible error log and
/// a footer with status, analysis time and Cancel.
/// </summary>
public sealed class AddingProgressWindowLayoutTests
{
    [AvaloniaFact]
    public void ProgressWindow_ShowsStatsAndLogWithoutTabs()
    {
        var viewModel = CreateViewModel();
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());

            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            Assert.Contains(LocalizationService.Current.Get(UiKeys.Filtering), texts);
            Assert.Contains(LocalizationService.Current.Get(UiKeys.VolumeStatistics), texts);
            Assert.Contains(LocalizationService.Current.Get(UiKeys.ScanStatistics), texts);
            Assert.Contains(LocalizationService.Current.Get(UiKeys.ErrorLog), texts);
            Assert.Contains(LocalizationService.Current.Get(UiKeys.AnalysisTime), texts);
            Assert.Contains("Reading source file...", texts);

            // The active source is shown inline on the filtering line, not in a dedicated left column.
            Assert.Contains(LocalizationService.Current.Get(UiKeys.FromFolder), texts);
            var root = Assert.IsType<Grid>(window.Content);
            Assert.Empty(root.ColumnDefinitions);

            var sourceIcons = window.GetVisualDescendants().OfType<Image>()
                .Where(image => image.Classes.Contains("source-icon"))
                .ToList();
            Assert.Equal(3, sourceIcons.Count);
            Assert.Single(sourceIcons, image => image.IsVisible);

            var statValues = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Classes.Contains("stat-value"))
                .ToList();
            Assert.Equal(8, statValues.Count);
            Assert.All(statValues, value => Assert.Equal(HorizontalAlignment.Right, value.HorizontalAlignment));
            Assert.Contains(statValues, value => value.Text == "1");

            var logViewer = window.FindControl<ScrollViewer>("LogScrollViewer");
            Assert.NotNull(logViewer);
            Assert.True(logViewer!.IsVisible);

            Assert.Single(window.GetVisualDescendants().OfType<ProgressBar>());
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancelButton_IsEnabledOnlyWhileCancellable()
    {
        var viewModel = new AddingProgressDialogViewModel { CanCancel = true };
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var cancel = FindCancelButton(window);
            Assert.True(cancel.IsEnabled);

            viewModel.CanCancel = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(cancel.IsEnabled);
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MinimumHeight_KeepsLogAndFooterInsideTheWindow()
    {
        var viewModel = new AddingProgressDialogViewModel { CanCancel = true };
        var window = new AddingProgressWindow { DataContext = viewModel, Height = 460 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var logViewer = window.FindControl<ScrollViewer>("LogScrollViewer")!;
            var cancel = FindCancelButton(window);

            Assert.True(logViewer.Bounds.Height >= 90,
                $"log pane collapsed to {logViewer.Bounds.Height} px at the minimum height.");
            Assert.True(BottomRelativeTo(logViewer, root) <= BottomRelativeTo(cancel, root),
                "the log pane overlaps the footer.");
            Assert.True(BottomRelativeTo(cancel, root) <= root.Bounds.Height + 1,
                "the footer is clipped at the minimum height.");
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    private static AddingProgressDialogViewModel CreateViewModel()
    {
        var viewModel = new AddingProgressDialogViewModel
        {
            CanCancel = true,
            SourceKind = AddingProgressSourceKind.Folder
        };
        viewModel.ReportStage("Reading source file...", stagePercentage: 25, overallPercentage: 25);
        viewModel.ReportLog(OperationLogLevel.ItemProcessed, "Processed item", itemPath: "/source/item");
        return viewModel;
    }

    private static Button FindCancelButton(Window window)
    {
        var label = LocalizationService.Current.Get(UiKeys.Cancel);
        return window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, label));
    }

    private static double BottomRelativeTo(Visual visual, Visual ancestor)
    {
        var origin = visual.TranslatePoint(default, ancestor);
        return origin!.Value.Y + visual.Bounds.Height;
    }
}
