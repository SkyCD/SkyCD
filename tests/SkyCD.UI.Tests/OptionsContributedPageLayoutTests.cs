using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Options;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class OptionsContributedPageLayoutTests
{
    [AvaloniaFact]
    public void ContributedPage_RendersItsContentInsideOptions()
    {
        var capability = new TestCapability();
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([
            new OptionsPluginItem("Locations", "test", "test", id: TestCapability.PluginId, isEnabled: true)
        ]);
        viewModel.SetPluginOptionsPages([
            new OptionsPageProvider(TestCapability.PluginId, "Locations", capability)
        ]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.ContributedPage is not null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowContributedOptionsPageSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Equal(1, capability.CreateCount);
            var rendered = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => ReferenceEquals(border, capability.Content));
            Assert.NotNull(rendered);
            Assert.True(rendered!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestCapability : IOptionsPageCapability
    {
        public const string PluginId = "skycd.locations";

        public int CreateCount { get; private set; }
        public Border Content { get; } = new();
        public OptionsPageDescriptor OptionsPage => new("locations", "Locations");
        public object CreateOptionsPage()
        {
            CreateCount++;
            return Content;
        }
    }
}
