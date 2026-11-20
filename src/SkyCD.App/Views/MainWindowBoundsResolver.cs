using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using SkyCD.Documents;

namespace SkyCD.App.Views;

internal readonly record struct ScreenWorkingArea(PixelRect WorkingArea, double Scaling, bool IsPrimary);

internal readonly record struct MainWindowLayout(
    double Width,
    double Height,
    PixelPoint? Position,
    WindowState State,
    double? TreePaneWidth);

internal static class MainWindowBoundsResolver
{
    private const double DefaultSizeRatio = 0.8;
    private const double MinimumDefaultWidth = 800;
    private const double MinimumDefaultHeight = 600;
    private const double PreferredDefaultWidth = 1280;
    private const double PreferredDefaultHeight = 800;

    /// <summary>
    /// Resolves the size, position, state and tree-pane width to apply before the window is shown.
    /// <see cref="MainWindowLayout.Position"/> is null when no screen geometry is available yet, so the
    /// caller can apply the size now and defer placement until screens become queryable.
    /// </summary>
    public static MainWindowLayout Resolve(WindowOptionsDocument options, IReadOnlyList<ScreenWorkingArea> screens)
    {
        var primary = FindPrimary(screens);
        var scaling = primary.Scaling > 0 ? primary.Scaling : 1;
        var availableWidth = screens.Count > 0 ? primary.WorkingArea.Width / scaling : double.PositiveInfinity;
        var availableHeight = screens.Count > 0 ? primary.WorkingArea.Height / scaling : double.PositiveInfinity;

        var width = options.Width is { } savedWidth && savedWidth > 0
            ? savedWidth
            : ResolveDefaultLength(PreferredDefaultWidth, MinimumDefaultWidth, availableWidth);
        var height = options.Height is { } savedHeight && savedHeight > 0
            ? savedHeight
            : ResolveDefaultLength(PreferredDefaultHeight, MinimumDefaultHeight, availableHeight);

        PixelPoint? position = null;
        if (screens.Count > 0)
        {
            position = options.Left.HasValue && options.Top.HasValue
                ? ClampPositionToVisibleBounds(
                    new PixelPoint(options.Left.Value, options.Top.Value),
                    width,
                    height,
                    screens)
                : CenterOnScreen(width, height, primary);
        }

        var treePaneWidth = options.TreePaneWidth is { } savedTreeWidth && savedTreeWidth > 0
            ? savedTreeWidth
            : (double?)null;

        return new MainWindowLayout(width, height, position, options.State, treePaneWidth);
    }

    private static ScreenWorkingArea FindPrimary(IReadOnlyList<ScreenWorkingArea> screens)
    {
        foreach (var screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        return screens.Count > 0 ? screens[0] : default;
    }

    private static double ResolveDefaultLength(double preferred, double minimum, double available)
    {
        if (double.IsInfinity(available) || available <= 0)
        {
            return preferred;
        }

        var lowerBound = Math.Min(minimum, available);
        var upperBound = Math.Min(preferred, available);
        return Math.Clamp(available * DefaultSizeRatio, lowerBound, upperBound);
    }

    private static PixelPoint CenterOnScreen(double width, double height, ScreenWorkingArea screen)
    {
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var pixelWidth = Math.Max(1, (int)Math.Round(width * scaling));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * scaling));
        var x = screen.WorkingArea.X + (screen.WorkingArea.Width - pixelWidth) / 2;
        var y = screen.WorkingArea.Y + (screen.WorkingArea.Height - pixelHeight) / 2;

        return ClampToScreen(new PixelPoint(x, y), pixelWidth, pixelHeight, screen.WorkingArea);
    }

    private static PixelPoint ClampPositionToVisibleBounds(PixelPoint requestedPosition, double requestedWidth,
        double requestedHeight, IReadOnlyList<ScreenWorkingArea> screens)
    {
        var windowWidth = Math.Max(1, (int)Math.Round(requestedWidth));
        var windowHeight = Math.Max(1, (int)Math.Round(requestedHeight));

        foreach (var screen in screens)
        {
            if (Intersects(requestedPosition, windowWidth, windowHeight, screen.WorkingArea))
            {
                return ClampToScreen(requestedPosition, windowWidth, windowHeight, screen.WorkingArea);
            }
        }

        return ClampToScreen(requestedPosition, windowWidth, windowHeight, FindPrimary(screens).WorkingArea);
    }

    private static bool Intersects(PixelPoint position, int width, int height, PixelRect bounds)
    {
        var right = position.X + width;
        var bottom = position.Y + height;

        return position.X < bounds.Right &&
               right > bounds.X &&
               position.Y < bounds.Bottom &&
               bottom > bounds.Y;
    }

    private static PixelPoint ClampToScreen(PixelPoint position, int width, int height, PixelRect bounds)
    {
        var maxX = Math.Max(bounds.X, bounds.Right - width);
        var maxY = Math.Max(bounds.Y, bounds.Bottom - height);

        var clampedX = Math.Clamp(position.X, bounds.X, maxX);
        var clampedY = Math.Clamp(position.Y, bounds.Y, maxY);
        return new PixelPoint(clampedX, clampedY);
    }
}
