using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    public void CloseMode_ShowsCloseInsteadOfCancelAndStaysEnabled()
    {
        var viewModel = new AddingProgressDialogViewModel { CanCancel = true };
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var button = FindDismissButton(window);
            Assert.Equal(LocalizationService.Current.Get(UiKeys.Cancel), button.Content);
            Assert.True(button.IsEnabled);

            viewModel.FinishCancellation();
            viewModel.IsCloseMode = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(LocalizationService.Current.Get(UiKeys.Close), button.Content);
            Assert.True(button.IsEnabled);
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

    /// <summary>
    /// Issue #1062: the footer keeps its three elements on one baseline, leaves a real gap before
    /// Cancel and stays inside the SkyInsetMd inset at the minimum window height.
    /// </summary>
    [AvaloniaFact]
    public void Footer_SharesOneBaselineAndKeepsAGapBeforeCancel()
    {
        var viewModel = CreateViewModel();
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var pleaseWait = FindText(window, UiKeys.PleaseWait);
            var analysisLabel = FindText(window, UiKeys.AnalysisTime);
            var analysisGroup = (Control)analysisLabel.Parent!;
            var cancel = FindCancelButton(window);

            var pleaseWaitCenter = CenterRelativeTo(pleaseWait, root);
            Assert.True(Math.Abs(CenterRelativeTo(analysisLabel, root) - pleaseWaitCenter) <= 1,
                "the analysis time does not share the status line baseline.");
            Assert.True(Math.Abs(CenterRelativeTo(cancel, root) - pleaseWaitCenter) <= 1,
                "the Cancel button is not centered with the footer text.");

            var groupRight = RightRelativeTo(analysisGroup, root);
            var cancelLeft = LeftRelativeTo(cancel, root);
            Assert.True(cancelLeft - groupRight >= 11,
                $"the analysis time is only {cancelLeft - groupRight} px from Cancel.");

            Assert.True(root.Margin.Bottom >= 11,
                "the content grid lost the window inset at the default size.");
            Assert.True(BottomRelativeTo(cancel, root) <= root.Bounds.Height,
                "the footer overflowed the window inset at the default size.");
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MinimumHeight_WithThumbnail_KeepsTheFooterInsideTheWindow()
    {
        var viewModel = new AddingProgressDialogViewModel
        {
            CanCancel = true,
            LatestThumbnail = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul)
        };
        var window = new AddingProgressWindow { DataContext = viewModel, Height = 460 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var scroller = window.FindControl<ScrollViewer>("ProgressContentScroller")!;
            var logViewer = window.FindControl<ScrollViewer>("LogScrollViewer")!;
            var cancel = FindCancelButton(window);

            Assert.True(logViewer.Bounds.Height >= 90,
                $"log pane collapsed to {logViewer.Bounds.Height} px at the minimum height.");
            Assert.True(BottomRelativeTo(scroller, root) <= TopRelativeTo(cancel, root),
                "the scrolling content overlaps the footer.");
            Assert.True(root.Margin.Bottom >= 11,
                "the content grid lost the window inset at the minimum height.");
            Assert.True(BottomRelativeTo(cancel, root) <= root.Bounds.Height,
                "the footer is pushed past the window edge at the minimum height.");
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShortWindow_ScrollsTheContentAndKeepsTheFooterPinned()
    {
        var viewModel = new AddingProgressDialogViewModel
        {
            CanCancel = true,
            LatestThumbnail = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul)
        };
        var window = new AddingProgressWindow { DataContext = viewModel, MinHeight = 200, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var scroller = window.FindControl<ScrollViewer>("ProgressContentScroller")!;
            var cancel = FindCancelButton(window);

            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                "the thumbnail and log should overflow a short window.");
            Assert.True(BottomRelativeTo(scroller, root) <= TopRelativeTo(cancel, root),
                "the scrolling content overlaps the footer.");
            Assert.True(BottomRelativeTo(cancel, root) <= root.Bounds.Height,
                "the footer is pushed past the window edge in a short window.");
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    /// <summary>
    /// Issue #1107: the outer scroller must not show a full-height scrollbar next to the log's own
    /// one when everything fits; the log takes the remaining height instead.
    /// </summary>
    [AvaloniaFact]
    public void DefaultSize_HasNoOuterScrollbarAndTheLogFillsTheRemainingHeight()
    {
        var viewModel = CreateViewModel();
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var scroller = window.FindControl<ScrollViewer>("ProgressContentScroller")!;
            var logViewer = window.FindControl<ScrollViewer>("LogScrollViewer")!;
            var cancel = FindCancelButton(window);

            AssertNoOuterVerticalScroll(scroller);
            Assert.True(logViewer.Bounds.Height > 90,
                $"the error log did not fill the remaining height ({logViewer.Bounds.Height} px).");
            Assert.True(BottomRelativeTo(logViewer, root) <= TopRelativeTo(cancel, root),
                "the error log overlaps the footer.");
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, logViewer.HorizontalScrollBarVisibility);
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    /// <summary>
    /// Issue #1107: shrinking to MinHeight and showing the thumbnail may scroll the blocks, but
    /// growing the window back must not leave the outer scrollbar behind.
    /// </summary>
    [AvaloniaFact]
    public void ResizingAndShowingThumbnail_DoesNotLeaveAStaleOuterScrollbar()
    {
        var viewModel = new AddingProgressDialogViewModel { CanCancel = true };
        var window = new AddingProgressWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var root = (Control)window.Content!;
            var scroller = window.FindControl<ScrollViewer>("ProgressContentScroller")!;
            var logViewer = window.FindControl<ScrollViewer>("LogScrollViewer")!;
            var cancel = FindCancelButton(window);

            viewModel.LatestThumbnail = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            window.Height = 460;
            Dispatcher.UIThread.RunJobs();
            window.Height = 540;
            Dispatcher.UIThread.RunJobs();

            AssertNoOuterVerticalScroll(scroller);
            Assert.True(logViewer.Bounds.Height >= 90,
                $"the error log collapsed to {logViewer.Bounds.Height} px after resizing.");
            Assert.True(BottomRelativeTo(cancel, root) <= root.Bounds.Height + 1,
                "the footer is clipped after resizing.");
        }
        finally
        {
            viewModel.FinishCancellation();
            window.Close();
        }
    }

    private static void AssertNoOuterVerticalScroll(ScrollViewer scroller)
    {
        Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
        Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
            $"the outer scroller overflows its viewport ({scroller.Extent.Height} > {scroller.Viewport.Height}).");
        Assert.DoesNotContain(scroller.GetVisualDescendants().OfType<ScrollBar>(),
            bar => bar.Name == "PART_VerticalScrollBar" && bar.IsVisible);

        var content = Assert.IsType<Grid>(scroller.Content);
        Assert.True(Math.Abs(content.Bounds.Width - scroller.Bounds.Width) <= 1,
            "the outer scroller reserves a vertical scrollbar gutter.");
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

    private static Button FindDismissButton(Window window)
    {
        var cancel = LocalizationService.Current.Get(UiKeys.Cancel);
        var close = LocalizationService.Current.Get(UiKeys.Close);
        return window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, cancel) || Equals(button.Content, close));
    }

    private static TextBlock FindText(Window window, string key)
    {
        var text = LocalizationService.Current.Get(key);
        return window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text);
    }

    private static double TopRelativeTo(Visual visual, Visual ancestor) =>
        visual.TranslatePoint(default, ancestor)!.Value.Y;

    private static double LeftRelativeTo(Visual visual, Visual ancestor) =>
        visual.TranslatePoint(default, ancestor)!.Value.X;

    private static double RightRelativeTo(Visual visual, Visual ancestor) =>
        visual.TranslatePoint(default, ancestor)!.Value.X + visual.Bounds.Width;

    private static double CenterRelativeTo(Visual visual, Visual ancestor) =>
        visual.TranslatePoint(default, ancestor)!.Value.Y + visual.Bounds.Height / 2;

    private static double BottomRelativeTo(Visual visual, Visual ancestor) =>
        visual.TranslatePoint(default, ancestor)!.Value.Y + visual.Bounds.Height;
}
