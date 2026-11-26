using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>Issue #931: each scan page scrolls inside the page instead of painting over the Save/Cancel footer.</summary>
public sealed class OptionsScanExclusionsLayoutTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [AvaloniaFact]
    public void ScanSettings_ScrollWithinThePageAndKeepTheFooterClear()
    {
        // The settings stack fits a 520px dialog after the completion block moved
        // to its own page. A shorter window is what makes this page scroll.
        var window = ShowScanWindow(out var viewModel, height: 420);
        try
        {
            Select(viewModel, OptionsDialogViewModel.ScanCategoryId);

            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(control => control.Name == "ScanSettingsScrollViewer");
            var saveButton = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.IsDefault);

            Assert.True(scroll.IsEffectivelyVisible);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Bounds.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height,
                $"Expected scrollable scan settings ({scroll.Extent.Height} > {scroll.Viewport.Height}); window {window.Bounds.Height} client {window.ClientSize.Height}.");
            Assert.False(FindRulesBorder(window).IsEffectivelyVisible);
            Assert.False(FindCompletionScroll(window).IsEffectivelyVisible);
            Assert.True(TranslateBottom(scroll, window) <= saveButton.TranslatePoint(default, window)!.Value.Y,
                "The scan settings must not reach the footer.");

            var settingsStack = scroll.GetVisualDescendants().OfType<StackPanel>()
                .First(panel => panel.GetVisualDescendants().OfType<CheckBox>().Any());
            var checkBoxes = settingsStack.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var saveErrors = checkBoxes[^1];
            var showErrors = checkBoxes[^2];

            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(showErrors.TranslatePoint(default, scroll)!.Value.Y, 0, scroll.Viewport.Height);
            Assert.InRange(saveErrors.TranslatePoint(default, scroll)!.Value.Y, 0, scroll.Viewport.Height);
            Assert.True(TranslateBottom(saveErrors, scroll) <= scroll.Viewport.Height + 0.5,
                $"The last option ({TranslateBottom(saveErrors, scroll)}) must land inside the viewport ({scroll.Viewport.Height}).");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScanRules_FillThePageWithoutCoveringTheFooter()
    {
        var window = ShowScanWindow(out var viewModel);
        try
        {
            Select(viewModel, OptionsDialogViewModel.ScanRulesCategoryId);

            var rulesBorder = FindRulesBorder(window);
            var rulesList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => control.Name == "ScanExclusionRulesList");
            var saveButton = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.IsDefault);

            Assert.True(rulesBorder.IsEffectivelyVisible);
            Assert.True(rulesBorder.Bounds.Height > 0);
            Assert.True(rulesList.Bounds.Height > 0);
            Assert.False(FindSettingsScroll(window).IsEffectivelyVisible);
            Assert.False(FindCompletionScroll(window).IsEffectivelyVisible);
            Assert.True(TranslateBottom(rulesBorder, window) <= saveButton.TranslatePoint(default, window)!.Value.Y,
                "The rules table must not reach the footer.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScanCompletion_ShowsOnlyTheFinishActionsAboveTheFooter()
    {
        var window = ShowScanWindow(out var viewModel);
        try
        {
            Select(viewModel, OptionsDialogViewModel.ScanCompletionCategoryId);

            var scroll = FindCompletionScroll(window);
            var saveButton = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.IsDefault);
            var visibleChecks = scroll.GetVisualDescendants().OfType<CheckBox>()
                .Where(checkBox => checkBox.IsEffectivelyVisible)
                .ToArray();

            Assert.True(scroll.IsEffectivelyVisible);
            Assert.Equal(3, visibleChecks.Length);
            Assert.False(FindSettingsScroll(window).IsEffectivelyVisible);
            Assert.False(FindRulesBorder(window).IsEffectivelyVisible);
            Assert.All(visibleChecks, checkBox =>
                Assert.True(TranslateBottom(checkBox, window) <= saveButton.TranslatePoint(default, window)!.Value.Y));
        }
        finally
        {
            window.Close();
        }
    }

    private static OptionsWindow ShowScanWindow(out OptionsDialogViewModel viewModel, double height = 520)
    {
        viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetScanExclusionRules([
            new ScanExclusionRuleDocument { Id = "rule-1", Pattern = "*.tmp" },
            new ScanExclusionRuleDocument { Id = "rule-2", Pattern = "bin", EntryType = "Folder" }
        ]);
        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        window.MinHeight = height;
        window.MaxHeight = height;
        window.Width = 760;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Select(OptionsDialogViewModel viewModel, string categoryId)
    {
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == categoryId);
        Dispatcher.UIThread.RunJobs();
    }

    private static ScrollViewer FindSettingsScroll(OptionsWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ScanSettingsScrollViewer");

    private static ScrollViewer FindCompletionScroll(OptionsWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ScanCompletionScrollViewer");

    private static Border FindRulesBorder(OptionsWindow window) =>
        window.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "ScanExclusionRulesBorder");

    private static double TranslateBottom(Visual visual, Visual relativeTo) =>
        visual.TranslatePoint(new Point(0, visual.Bounds.Height), relativeTo)!.Value.Y;
}
