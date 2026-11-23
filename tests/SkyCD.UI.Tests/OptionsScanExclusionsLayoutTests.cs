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

/// <summary>Issue #931: scan settings scroll inside the page instead of painting over the Save/Cancel footer.</summary>
public sealed class OptionsScanExclusionsLayoutTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [AvaloniaFact]
    public void ScanSettings_ScrollWithinThePageAndKeepFooterAndRulesReachable()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetScanExclusionRules([
            new ScanExclusionRuleDocument { Id = "rule-1", Pattern = "*.tmp" },
            new ScanExclusionRuleDocument { Id = "rule-2", Pattern = "bin", EntryType = "Folder" }
        ]);
        viewModel.SelectedTabIndex = ScanCategoryIndex;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowScanSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        window.Width = 760;
        window.Height = 520;
        Dispatcher.UIThread.RunJobs();

        try
        {
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(control => control.Name == "ScanSettingsScrollViewer");
            var rulesBorder = window.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Name == "ScanExclusionRulesBorder");
            var rulesList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => control.Name == "ScanExclusionRulesList");
            var saveButton = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.IsDefault);

            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Bounds.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height,
                $"Expected scrollable scan settings ({scroll.Extent.Height} > {scroll.Viewport.Height}).");

            Assert.True(rulesBorder.Bounds.Height > 0,
                "The exclusion rules table must keep visible space at the default window size.");
            Assert.True(rulesList.Bounds.Height > 0,
                "The exclusion rules list must be reachable at the default window size.");

            // The footer band starts below the settings viewport and the rules table: no occlusion.
            var scrollBottom = TranslateBottom(scroll, window);
            var rulesBottom = TranslateBottom(rulesBorder, window);
            var saveTop = saveButton.TranslatePoint(default, window)!.Value.Y;
            Assert.True(scrollBottom <= saveTop,
                $"The scan settings ({scrollBottom}) must not reach the footer ({saveTop}).");
            Assert.True(rulesBottom <= saveTop,
                $"The rules table ({rulesBottom}) must not reach the footer ({saveTop}).");

            // The last completion options become visible after scrolling to the end.
            var settingsStack = scroll.GetVisualDescendants().OfType<StackPanel>()
                .First(panel => panel.GetVisualDescendants().OfType<CheckBox>().Any());
            var checkBoxes = settingsStack.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var playSound = checkBoxes[^1];
            var activateMainWindow = checkBoxes[^2];

            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();

            var activateTop = activateMainWindow.TranslatePoint(default, scroll)!.Value.Y;
            var playSoundTop = playSound.TranslatePoint(default, scroll)!.Value.Y;
            var playSoundBottom = TranslateBottom(playSound, scroll);
            Assert.InRange(activateTop, 0, scroll.Viewport.Height);
            Assert.InRange(playSoundTop, 0, scroll.Viewport.Height);
            Assert.True(playSoundBottom <= scroll.Viewport.Height + 0.5,
                $"The last option ({playSoundBottom}) must land inside the viewport ({scroll.Viewport.Height}).");
        }
        finally
        {
            window.Close();
        }
    }

    private const int ScanCategoryIndex = 6;

    private static double TranslateBottom(Visual visual, Visual relativeTo) =>
        visual.TranslatePoint(new Point(0, visual.Bounds.Height), relativeTo)!.Value.Y;
}
