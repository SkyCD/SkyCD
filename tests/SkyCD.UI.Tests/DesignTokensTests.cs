using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #898: dialogs consume the shared design tokens (spacing ramp, type scale, corner radius,
/// font family) instead of declaring their own literals.
/// </summary>
public sealed class DesignTokensTests
{
    private static object Token(string key)
    {
        var resources = Application.Current!.Resources;
        Assert.True(resources.TryGetResource(key, null, out var value), $"Token '{key}' is missing.");
        return value!;
    }

    private static double Size(string key) => (double)Token(key);

    [AvaloniaFact]
    public void TokenDictionary_DefinesTheSpacingRampAndTypeScale()
    {
        Assert.Equal(2, Size("SkySpaceXxs"));
        Assert.Equal(4, Size("SkySpaceXs"));
        Assert.Equal(8, Size("SkySpaceSm"));
        Assert.Equal(12, Size("SkySpaceMd"));
        Assert.Equal(16, Size("SkySpaceLg"));
        Assert.Equal(24, Size("SkySpaceXl"));
        Assert.Equal(32, Size("SkySpaceXxl"));

        Assert.Equal(11, Size("SkyFontCaption"));
        Assert.Equal(12, Size("SkyFontSecondary"));
        Assert.Equal(14, Size("SkyFontBody"));
        Assert.Equal(16, Size("SkyFontSubtitle"));
        Assert.Equal(20, Size("SkyFontTitle"));
        Assert.Equal(24, Size("SkyFontWindowTitle"));

        Assert.Equal(new CornerRadius(4), Token("SkyCornerRadius"));
        Assert.Equal(new Thickness(12), Token("SkyWindowPadding"));

        var family = Assert.IsType<FontFamily>(Token("SkyFontFamily"));
        Assert.Contains("Segoe UI", family.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Insets_OnlyMoveTheSidesTheirNameNames()
    {
        Assert.Equal(new Thickness(0, 2, 0, 0), Token("SkyGapTopXxs"));
        Assert.Equal(new Thickness(0, 4, 0, 0), Token("SkyGapTopXs"));
        Assert.Equal(new Thickness(0, 8, 0, 0), Token("SkyGapTopSm"));
        Assert.Equal(new Thickness(0, 12, 0, 0), Token("SkyGapTopMd"));
        Assert.Equal(new Thickness(0, 16, 0, 0), Token("SkyGapTopLg"));
        Assert.Equal(new Thickness(0, 0, 0, 2), Token("SkyGapBottomXxs"));
        Assert.Equal(new Thickness(0, 0, 0, 4), Token("SkyGapBottomXs"));
        Assert.Equal(new Thickness(0, 0, 0, 8), Token("SkyGapBottomSm"));
        Assert.Equal(new Thickness(0, 0, 0, 12), Token("SkyGapBottomMd"));
        Assert.Equal(new Thickness(8, 0, 0, 0), Token("SkyGapLeftSm"));
        Assert.Equal(new Thickness(0, 2, 0, 2), Token("SkyGapYXxs"));
        Assert.Equal(new Thickness(0, 4, 0, 4), Token("SkyGapYXs"));
        Assert.Equal(new Thickness(0, 8, 0, 8), Token("SkyGapYSm"));
        Assert.Equal(new Thickness(0, 12, 0, 12), Token("SkyGapYMd"));
        Assert.Equal(new Thickness(2, 0, 2, 0), Token("SkyGapXXxs"));
        Assert.Equal(new Thickness(4, 0, 4, 0), Token("SkyGapXXs"));
    }

    [AvaloniaFact]
    public void Inputs_ShareOneInnerInsetAndSearchIconSize()
    {
        // Issue #1184: every text input uses the same inner inset, and the shared search field
        // draws its magnifier at one token size. The inset matches the combo content padding so
        // input text and the search icon start at one shared edge.
        Assert.Equal(Token("SkyComboBoxContentPadding"), Token("SkyInputPadding"));
        Assert.Equal(new Thickness(8, 4), Token("SkyInputPadding"));
        Assert.Equal(14, Size("SkySearchIconSize"));
    }

    [AvaloniaFact]
    public void Insets_KeepThePairsThatLineMarkupDependsOn()
    {
        // The inline property editor cancels the cell padding it sits in, and the selected tab card
        // overlaps exactly the 1px border it meets, so both stay tied to these values.
        Assert.Equal(new Thickness(-8, -4), Token("SkyControlPaddingInverse"));
        Assert.Equal(new Thickness(8, 4), Token("SkyControlPadding"));
        Assert.Equal(new Thickness(0, -1, 0, 0), Token("SkyTabCardBorderOverlap"));
        Assert.Equal(new Thickness(0, 0, 0, 1), Token("SkyTabCardHeaderHoverInset"));
    }

    [AvaloniaFact]
    public void Insets_KeepTheEdgesThatTwoSurfacesShare()
    {
        // The card tab header text and the card content are one left edge (issue #997), so the header
        // inset follows the shared content inset rather than its own ramp step.
        var cardInset = Assert.IsType<Thickness>(Token("SkyInsetMd"));
        var headerPadding = Assert.IsType<Thickness>(Token("SkyTabHeaderPadding"));
        Assert.Equal(cardInset.Left, headerPadding.Left);
        Assert.Equal(cardInset.Right, headerPadding.Right);

        // The 25px toolbar leaves exactly 1px above and below the 22px buttons it holds (issue #412).
        Assert.Equal(new Thickness(2, 1), Token("SkyToolbarPadding"));
    }

    [AvaloniaFact]
    public void About_UsesWindowPaddingAndTypeScaleTokens()
    {
        using var fixture = ShowAbout(ThemeVariant.Light);

        var root = Assert.IsType<DockPanel>(fixture.Window.Content);
        Assert.Equal((Thickness)Token("SkyWindowPadding"), root.Margin);

        var title = fixture.Window.FindControl<TextBlock>("ProductNameText")!;
        Assert.Equal(Size("SkyFontTitle"), title.FontSize);
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);

        var licenseBox = fixture.Window.FindControl<Border>("LicenseBox")!;
        Assert.Equal(new CornerRadius(4), licenseBox.CornerRadius);
        Assert.Equal(new Thickness(1), licenseBox.BorderThickness);
        Assert.NotNull(licenseBox.BorderBrush);

        var licenseScroll = fixture.Window.FindControl<ScrollViewer>("LicenseScroll")!;
        Assert.Equal(new Thickness(8), licenseScroll.Padding);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            licenseScroll.HorizontalScrollBarVisibility);
    }

    [AvaloniaFact]
    public void About_RendersInDarkAndLightThemes()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            using var fixture = ShowAbout(variant);
            var licenseBox = fixture.Window.FindControl<Border>("LicenseBox")!;

            // The license surface follows the active theme instead of a light-only literal.
            Assert.Equal(variant, fixture.Window.ActualThemeVariant);
            Assert.NotNull(licenseBox.Background);
            Assert.NotNull(licenseBox.BorderBrush);
        }
    }

    [AvaloniaFact]
    public void GlobalStyles_ApplyCompactAndHeadingScale()
    {
        var title = new TextBlock { Classes = { "h1" } };
        var caption = new TextBlock { Classes = { "caption" } };
        var window = new Window
        {
            Classes = { "compact" },
            Content = new StackPanel { Children = { title, caption, new TextBlock { Name = "Body" } } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var body = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "Body");
            Assert.Equal(Size("SkyFontTitle"), title.FontSize);
            Assert.Equal(Size("SkyFontCaption"), caption.FontSize);
            Assert.Equal(Size("SkyFontCaption"), body.FontSize);
            Assert.Contains("Segoe UI", window.FontFamily.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    private static AboutFixture ShowAbout(ThemeVariant variant)
    {
        var window = new AboutWindow
        {
            DataContext = new AboutDialogViewModel(),
            RequestedThemeVariant = variant,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new AboutFixture(window);
    }

    private sealed class AboutFixture(AboutWindow window) : IDisposable
    {
        public AboutWindow Window { get; } = window;

        public void Dispose() => Window.Close();
    }
}
