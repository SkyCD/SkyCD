using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia;
using IconPacks.Avalonia.Lucide;
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
    public void InstalledPlugins_ShowNeutralGlyphWhenTheLibraryShipsNoIcon()
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
            var rowIcons = window.GetVisualDescendants()
                .OfType<PackIconControl>()
                .Where(icon => icon.DataContext is OptionsPluginItem)
                .ToArray();

            Assert.NotEmpty(rowIcons);
            Assert.All(rowIcons, icon =>
            {
                Assert.Equal(PackIconLucideKind.Puzzle, icon.Kind);
                Assert.True(icon.IsVisible);
            });

            Assert.DoesNotContain(window.GetVisualDescendants()
                .OfType<Image>(), image => image.DataContext is OptionsPluginItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InstalledPlugins_ShowTheIconEmbeddedInTheLibrary()
    {
        var icon = System.Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins([new OptionsPluginItem("JSON", "SkyCD", "json v1", id: "json", icon: icon)]);
        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var glyph = Assert.Single(window.GetVisualDescendants().OfType<PackIconControl>(),
                candidate => candidate.DataContext is OptionsPluginItem);
            Assert.Equal(PackIconLucideKind.Puzzle, glyph.Kind);
            Assert.False(glyph.IsVisible);

            var mask = Assert.Single(window.GetVisualDescendants().OfType<Border>(),
                border => border.DataContext is OptionsPluginItem && border.OpacityMask is not null);
            Assert.True(mask.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
