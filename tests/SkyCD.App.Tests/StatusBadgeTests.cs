using System.Globalization;
using Avalonia;
using Avalonia.Media;
using IconPacks.Avalonia;
using SkyCD.App.Converters;
using SkyCD.App.Icons;
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
        StatusIconCatalog.RegisterKeys(StatusIconResolver.GetAllKeys());
    }

    [Fact]
    public void StatusGlyph_RendersIconInsteadOfItsKey()
    {
        const string key = "Lucide:Handshake";
        var converter = new StatusBadgeIconConverter();
        Assert.Contains(key, StatusIconCatalog.GetAllKeys());
        Assert.Contains(key, StatusIconResolver.GetAllKeys());

        var icon = Assert.IsType<PackIconControl>(
            converter.Convert(key, typeof(object), null, CultureInfo.InvariantCulture));

        Assert.True(StatusIconResolver.TryResolveKind(key, out var expected));
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

    [Theory]
    [InlineData("")]
    [InlineData("unknown:status")]
    public void MenuStatusIcon_WithNoRenderableGlyph_HasNoIcon(string key)
    {
        var converter = new MenuIconConverter();
        var status = new StatusMenuIcon(key, "#FF0000");

        Assert.Null(converter.Convert(status, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void IconOptions_ExposeRegisteredKeysWithoutAvaloniaTypes()
    {
        try
        {
            StatusIconCatalog.RegisterKeys(["Lucide:Handshake", "Material:Star"]);

            var viewModel = new StatusVariantItemViewModel();

            Assert.Equal(["Lucide:Handshake", "Material:Star"], viewModel.IconOptions);
            Assert.True(StatusIconCatalog.IsKnownKey("lucide:handshake"));
            Assert.False(StatusIconCatalog.IsKnownKey("unknown:status"));
            Assert.False(StatusIconCatalog.IsKnownKey(null));
        }
        finally
        {
            StatusIconCatalog.RegisterKeys(StatusIconResolver.GetAllKeys());
        }
    }

    [Theory]
    [InlineData("not-a-color")]
    [InlineData(null)]
    public void InvalidColor_ResolvesWhiteBrush(string? color)
    {
        Assert.Equal(Brushes.White, StatusIconResolver.ResolveBrush(color));
    }
}
