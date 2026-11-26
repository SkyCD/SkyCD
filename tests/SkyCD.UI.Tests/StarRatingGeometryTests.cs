using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SkyCD.UI.Controls.Rating;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Rendering cover for the star outline behind each rating cell: the vector geometry must be a real,
/// non-empty star, and the filled and empty palettes must differ, so an "invisible stars" regression
/// fails here instead of only showing visually in Properties > General.
/// </summary>
/// <remarks>
/// Headless runs stub out rasterization (RenderTargetBitmap saves nothing) and approximate
/// hit-testing with per-triangle checks, so these tests assert the geometry extents and a
/// spike-interior hit — both exact under the stub — instead of pixels. The identical point
/// sequence was verified to rasterize as a solid star against Skia directly.
/// </remarks>
public class StarRatingGeometryTests
{
    // The reference star spans x 1.5..22.5 and y 1..21 on its 24-unit grid.
    private const double StarWidthUnits = 21;
    private const double StarHeightUnits = 20;
    private const double ReferenceBox = 24;

    [AvaloniaFact]
    public void StarGeometry_IsNonEmptyAndMatchesTheStarExtents()
    {
        var geometry = StarRatingControl.CreateStarGeometry(new Rect(0, 0, 18, 18));

        Assert.True(geometry.Bounds.Width > 0, "The star geometry must have a width.");
        Assert.True(geometry.Bounds.Height > 0, "The star geometry must have a height.");
        Assert.Equal(18 * StarWidthUnits / ReferenceBox, geometry.Bounds.Width, 1);
        Assert.Equal(18 * StarHeightUnits / ReferenceBox, geometry.Bounds.Height, 1);

        // The top spike interior is filled under every backend; the pre-fix parser never began
        // a figure, so even this spike tested empty.
        Assert.True(
            geometry.FillContains(new Point(9, 5)),
            "The top spike interior must fall inside the filled star.");
        Assert.False(
            geometry.FillContains(new Point(1, 1)),
            "A corner of the cell must fall outside the star.");
    }

    [AvaloniaTheory]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(32)]
    public void StarGeometry_ScalesWithStarSize(double starSize)
    {
        var geometry = StarRatingControl.CreateStarGeometry(new Rect(0, 0, starSize, starSize));

        Assert.Equal(starSize * StarWidthUnits / ReferenceBox, geometry.Bounds.Width, 1);
        Assert.Equal(starSize * StarHeightUnits / ReferenceBox, geometry.Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void StarPalette_FilledAndEmptyDiffer()
    {
        // The documented palette (gold #E5B045 filled, gray #8A8F98 empty) is what makes a
        // rated star visually distinct from an unrated one once the geometry paints.
        var filled = (Color)typeof(StarRatingControl)
            .GetField("FilledColor", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var empty = (Color)typeof(StarRatingControl)
            .GetField("EmptyColor", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.Equal(Color.Parse("#E5B045"), filled);
        Assert.Equal(Color.Parse("#8A8F98"), empty);
        Assert.NotEqual(empty, filled);
    }
}
