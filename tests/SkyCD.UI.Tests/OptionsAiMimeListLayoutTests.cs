using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>Issue #818: the Excluded MIME types list fills the AI description page instead of a fixed 150 px box.</summary>
public sealed class OptionsAiMimeListLayoutTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [AvaloniaFact]
    public void AiMimeListBorder_GrowsWithThePageAndScrollsInternally()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAiDescriptionAvailable(true);
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == "ai-description");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowAiDescriptionSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var border = window.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Name == "AiScanExclusionsListBorder");
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => control.Name == "AiScanExclusionsList");

            // No fixed box: the border takes whatever the page leaves in the star row.
            Assert.True(double.IsNaN(border.Height));
            Assert.True(double.IsNaN(list.Height));
            Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(list));

            var smallHeight = border.Bounds.Height;
            Assert.True(smallHeight > 0);

            var listRow = (Grid)border.Parent!;
            listRow.RowDefinitions[1].Height = new GridLength(listRow.Bounds.Height + 200);
            window.Height += 200;
            Dispatcher.UIThread.RunJobs();

            Assert.True(border.Bounds.Height > smallHeight,
                $"Expected the MIME list to grow ({smallHeight} -> {border.Bounds.Height}).");
        }
        finally
        {
            window.Close();
        }
    }
}
