using Avalonia;
using Avalonia.Controls;
using SkyCD.App.Views;
using SkyCD.Documents;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class MainWindowBoundsTests
{
    private static readonly ScreenWorkingArea PrimaryScreen = new(new PixelRect(0, 0, 1920, 1080), 1, true);

    [Fact]
    public void Resolve_AppliesSavedBoundsBeforeShow()
    {
        var options = new WindowOptionsDocument
        {
            Left = 120,
            Top = 80,
            Width = 1200,
            Height = 850,
            State = WindowState.Maximized,
            TreePaneWidth = 320
        };

        var layout = MainWindowBoundsResolver.Resolve(options, [PrimaryScreen]);

        Assert.Equal(1200, layout.Width);
        Assert.Equal(850, layout.Height);
        Assert.Equal(new PixelPoint(120, 80), layout.Position);
        Assert.Equal(WindowState.Maximized, layout.State);
        Assert.Equal(320, layout.TreePaneWidth);
    }

    [Fact]
    public void Resolve_ClampsSavedPositionFromDisconnectedScreen()
    {
        var options = new WindowOptionsDocument
        {
            Left = 5000,
            Top = 5000,
            Width = 1000,
            Height = 700
        };

        var layout = MainWindowBoundsResolver.Resolve(options, [PrimaryScreen]);

        Assert.Equal(new PixelPoint(920, 380), layout.Position);
    }

    [Fact]
    public void Resolve_KeepsSavedPositionOnConnectedSecondaryScreen()
    {
        var secondaryScreen = new ScreenWorkingArea(new PixelRect(1920, 0, 1920, 1080), 1, false);
        var options = new WindowOptionsDocument
        {
            Left = 2000,
            Top = 100,
            Width = 800,
            Height = 600
        };

        var layout = MainWindowBoundsResolver.Resolve(options, [PrimaryScreen, secondaryScreen]);

        Assert.Equal(new PixelPoint(2000, 100), layout.Position);
    }

    [Fact]
    public void Resolve_FirstLaunchUsesCenteredDefaultSize()
    {
        var layout = MainWindowBoundsResolver.Resolve(new WindowOptionsDocument(), [PrimaryScreen]);

        Assert.Equal(1280, layout.Width);
        Assert.Equal(800, layout.Height);
        Assert.Equal(new PixelPoint(320, 140), layout.Position);
        Assert.Equal(WindowState.Normal, layout.State);
        Assert.Null(layout.TreePaneWidth);
    }

    [Fact]
    public void Resolve_DefaultSizeFitsSmallWorkingArea()
    {
        var smallScreen = new ScreenWorkingArea(new PixelRect(0, 0, 1024, 768), 1, true);

        var layout = MainWindowBoundsResolver.Resolve(new WindowOptionsDocument(), [smallScreen]);

        Assert.Equal(819.2, layout.Width, 3);
        Assert.Equal(614.4, layout.Height, 3);
        Assert.InRange(layout.Width, 503, 1024);
        Assert.InRange(layout.Height, 273, 768);
    }

    [Fact]
    public void Resolve_DefaultSizeUsesPrimaryScaling()
    {
        var highDpiScreen = new ScreenWorkingArea(new PixelRect(0, 0, 3840, 2160), 2, true);

        var layout = MainWindowBoundsResolver.Resolve(new WindowOptionsDocument(), [highDpiScreen]);

        Assert.Equal(1280, layout.Width);
        Assert.Equal(800, layout.Height);
        Assert.Equal(new PixelPoint(640, 280), layout.Position);
    }

    [Fact]
    public void Resolve_WithoutScreenGeometryDefersPositionButAppliesSavedSize()
    {
        var options = new WindowOptionsDocument
        {
            Left = 10,
            Top = 20,
            Width = 1200,
            Height = 850,
            TreePaneWidth = 300
        };

        var layout = MainWindowBoundsResolver.Resolve(options, []);

        Assert.Equal(1200, layout.Width);
        Assert.Equal(850, layout.Height);
        Assert.Null(layout.Position);
        Assert.Equal(300, layout.TreePaneWidth);
    }

    [Fact]
    public void Resolve_WithoutScreenGeometryUsesPreferredDefaultSize()
    {
        var layout = MainWindowBoundsResolver.Resolve(new WindowOptionsDocument(), []);

        Assert.Equal(1280, layout.Width);
        Assert.Equal(800, layout.Height);
        Assert.Null(layout.Position);
    }

    [Theory]
    [InlineData(WindowState.Normal, WindowState.Normal, 20, 800)]
    [InlineData(WindowState.Maximized, WindowState.Maximized, 10, 1200)]
    [InlineData(WindowState.Minimized, WindowState.Normal, 10, 1200)]
    public void Capture_PersistsNormalBoundsAndRestorableState(WindowState currentState,
        WindowState savedState, int savedLeft, double savedWidth)
    {
        var options = new WindowOptionsDocument { Left = 10, Top = 15, Width = 1200, Height = 850 };

        MainWindowPlacement.Capture(options, currentState, new PixelPoint(20, 25), 800, 600, new GridLength(320));

        Assert.Equal(savedState, options.State);
        Assert.Equal(savedLeft, options.Left);
        Assert.Equal(savedLeft + 5, options.Top);
        Assert.Equal(savedWidth, options.Width);
        Assert.Equal(currentState == WindowState.Normal ? 600 : 850, options.Height);
        Assert.Equal(320, options.TreePaneWidth);
    }

    [Fact]
    public void Capture_KeepsSavedTreeWidthForProportionalLayout()
    {
        var options = new WindowOptionsDocument { TreePaneWidth = 320 };

        MainWindowPlacement.Capture(options, WindowState.Normal, default, 800, 600, GridLength.Star);

        Assert.Equal(320, options.TreePaneWidth);
    }
}
