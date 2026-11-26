using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1147: the System info tab samples only while it is selected, so a hidden tab does not
/// keep a timer and process probes running.
/// </summary>
public sealed class AboutSystemInfoSamplingTests
{
    [AvaloniaFact]
    public void About_SamplesSystemInfoOnlyWhileTheTabIsSelected()
    {
        var viewModel = new AboutDialogViewModel();
        var window = Show(new AboutWindow { DataContext = viewModel });
        try
        {
            Assert.False(window.IsSystemInfoSampling);

            viewModel.RestoreSelectedTab(AboutDialogViewModel.SystemInfoTabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsSystemInfoSampling);

            viewModel.RestoreSelectedTab(AboutDialogViewModel.AboutStoryTabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsSystemInfoSampling);
        }
        finally
        {
            window.Close();
        }
    }

    private static AboutWindow Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (AboutWindow)window;
    }
}
