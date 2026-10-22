using System.Globalization;
using Avalonia;
using Avalonia.Media;
using IconPacks.Avalonia;
using SkyCD.App.Converters;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class StatusBadgeTests
{
    static StatusBadgeTests()
    {
        var builder = AppBuilder.Configure<Application>()
            .UseStandardRuntimePlatformSubsystem()
            .UseSkia();
        builder.RuntimePlatformServicesInitializer!();
        builder.RenderingSubsystemInitializer!();
    }

    [Fact]
    public void StatusGlyph_RendersIconInsteadOfItsKey()
    {
        const string key = "Lucide:Handshake";
        var converter = new StatusBadgeIconConverter();
        Assert.Contains(key, StatusIconCatalog.GetAllKeys());

        var icon = Assert.IsType<PackIconControl>(
            converter.Convert(key, typeof(object), null, CultureInfo.InvariantCulture));

        Assert.True(StatusIconCatalog.TryResolveKind(key, out var expected));
        Assert.Equal(expected, icon.Kind);
        Assert.False(icon.IsSet(PackIconControl.ForegroundProperty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown:status")]
    public void InvalidStatusGlyph_DoesNotRenderText(string key)
    {
        var converter = new StatusBadgeIconConverter();

        Assert.Null(converter.Convert(key, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MenuStatusIcon_PreservesConfiguredColor()
    {
        const string key = "Lucide:Handshake";
        var converter = new MenuIconConverter();
        var status = new StatusMenuIcon(key, "#FF0000");

        var icon = Assert.IsType<PackIconControl>(
            converter.Convert(status, typeof(object), null, CultureInfo.InvariantCulture));

        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
    }
}
