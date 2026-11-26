using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using SkyCD.Documents;

namespace SkyCD.App.Views;

internal static class MainWindowPlacement
{
    public static bool Apply(Window window, ColumnDefinition treePaneColumn, WindowOptionsDocument options)
    {
        var layout = MainWindowBoundsResolver.Resolve(options, GetScreenWorkingAreas(window));

        window.Width = layout.Width;
        window.Height = layout.Height;

        if (layout.Position is { } position)
        {
            window.Position = position;
        }

        if (layout.TreePaneWidth is { } treePaneWidth)
        {
            treePaneColumn.Width = new GridLength(treePaneWidth, GridUnitType.Pixel);
        }

        window.WindowState = layout.State;
        return layout.Position is null;
    }

    private static IReadOnlyList<ScreenWorkingArea> GetScreenWorkingAreas(Window window)
    {
        try
        {
            return window.Screens.All
                .Select(static screen => new ScreenWorkingArea(screen.WorkingArea, screen.Scaling, screen.IsPrimary))
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            // Screens may not be queryable on some backends until the window is shown.
            return [];
        }
    }

    public static void Capture(WindowOptionsDocument options, WindowState state, PixelPoint position,
        double width, double height, GridLength treePaneWidth)
    {
        // Don't save window position if window is minimized
        if (state == WindowState.Normal)
        {
            options.Left = position.X;
            options.Top = position.Y;
            options.Width = width;
            options.Height = height;
            options.State = WindowState.Normal;
        }
        else if (state == WindowState.Maximized)
        {
            options.State = WindowState.Maximized;
        }

        if (treePaneWidth.IsAbsolute)
        {
            options.TreePaneWidth = treePaneWidth.Value;
        }
    }
}
