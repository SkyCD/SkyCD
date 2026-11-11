using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkyCD.UI.Controls.Toolbars;
using Xunit;

namespace SkyCD.UI.Tests;

public class ClassicToolbarVisualTests
{
    [AvaloniaTheory]
    [InlineData("Light", 1.0)]
    [InlineData("Light", 1.5)]
    [InlineData("Dark", 1.0)]
    [InlineData("Dark", 1.5)]
    public void ClassicToolbar_RendersVisualStatesAtSpecifiedScaleAndTheme(string themeName, double scale)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        var window = new Window
        {
            Width = 503,
            Height = 100,
            RequestedThemeVariant = theme
        };

        var toolbar = new ClassicToolbar();
        var btnNew = new ClassicToolbarButton { Text = "_New" };
        var btnOpen = new ClassicToolbarButton { Text = "_Open" };
        var btnSave = new ClassicToolbarButton { Text = "_Save", IsEnabled = false };

        toolbar.Items.Add(btnNew);
        toolbar.Items.Add(btnOpen);
        toolbar.Items.Add(btnSave);

        window.Content = toolbar;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(25, toolbar.Bounds.Height);
        Assert.True(btnNew.IsEnabled);
        Assert.True(btnOpen.IsEnabled);
        Assert.False(btnSave.IsEnabled);

        var pixelWidth = Math.Max(1, (int)(window.Width * scale));
        var pixelHeight = Math.Max(1, (int)(toolbar.Bounds.Height * scale));
        var dpi = new Vector(96 * scale, 96 * scale);

        var rtb = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), dpi);
        rtb.Render(toolbar);

        Assert.Equal(pixelWidth, rtb.PixelSize.Width);
        Assert.Equal(pixelHeight, rtb.PixelSize.Height);

        window.Close();
    }
}
