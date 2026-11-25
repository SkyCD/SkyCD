using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SkyCD.App.Icons;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1140: commands without a fitting emoji keep their project-owned monochrome glyphs.
/// Resolving them paints a real drawing through the fallback branch, which also proves the
/// glyph dictionary entries parse.
/// </summary>
public sealed class ToolbarIconFallbackTests
{
    [AvaloniaTheory]
    [InlineData(ToolbarIconKeys.AddItem)]
    [InlineData(ToolbarIconKeys.ViewSmallIcons)]
    [InlineData(ToolbarIconKeys.ViewLargeIcons)]
    [InlineData(ToolbarIconKeys.ViewList)]
    [InlineData(ToolbarIconKeys.StatusBar)]
    [InlineData(ToolbarIconKeys.Options)]
    public void UnmappedCommands_FallBackToTheirMonochromeGlyphs(string key)
    {
        var image = ToolbarIconResolver.Resolve(key);

        var drawing = Assert.IsAssignableFrom<DrawingImage>(image);
        var group = Assert.IsAssignableFrom<DrawingGroup>(drawing.Drawing);
        Assert.NotEmpty(group.Children);
    }
}
