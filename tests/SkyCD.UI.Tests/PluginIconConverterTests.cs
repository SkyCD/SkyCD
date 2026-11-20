using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia;
using SkyCD.App.Converters;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class PluginIconConverterTests
{
    [AvaloniaFact]
    public void Convert_ResolvesExtensionIcon()
    {
        var converter = new PluginIconConverter();

        var icon = converter.Convert("ext:json", typeof(object), null, CultureInfo.InvariantCulture);

        Assert.NotNull(icon);
    }

    [AvaloniaFact]
    public void Convert_WithoutIconKeyReturnsNull()
    {
        var converter = new PluginIconConverter();

        var icon = converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Null(icon);
    }

    [AvaloniaFact]
    public void InstalledPlugins_ShowFormatIconOrFallback()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins(
        [
            new OptionsPluginItem("JSON", "SkyCD", "json v1", id: "json", primaryExtension: "json"),
            new OptionsPluginItem("Accounts", "SkyCD", "accounts v1", id: "accounts")
        ]);
        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var pluginImages = window.GetVisualDescendants()
                .OfType<Image>()
                .Where(image => image.DataContext is OptionsPluginItem)
                .ToArray();
            var fallbackIcons = window.GetVisualDescendants()
                .OfType<PackIconControl>()
                .Where(icon => icon.DataContext is OptionsPluginItem && icon.IsVisible)
                .ToArray();

            Assert.NotEmpty(pluginImages);
            Assert.NotEmpty(fallbackIcons);
        }
        finally
        {
            window.Close();
        }
    }
}
