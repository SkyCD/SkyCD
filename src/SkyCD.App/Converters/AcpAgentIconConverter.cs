using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SkyCD.App.Converters;

public sealed class AcpAgentIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> { Count: > 0 } paths) return null;
        var drawing = new DrawingGroup();
        foreach (var path in paths)
        {
            try
            {
                drawing.Children.Add(new GeometryDrawing { Geometry = StreamGeometry.Parse(path), Brush = Brushes.White });
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return new DrawingImage { Drawing = drawing };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
