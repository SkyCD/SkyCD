using System;
using System.IO;
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
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>Issue #818: the Excluded MIME types list fills the AI description page instead of a fixed 150 px box.</summary>
public sealed class OptionsAiMimeListLayoutTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-options-ai-mime-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public OptionsAiMimeListLayoutTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    [AvaloniaFact]
    public void AiMimeListBorder_GrowsWithThePageAndScrollsInternally()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAiDescriptionAvailable(true);
        viewModel.SelectedTabIndex = 10;
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
