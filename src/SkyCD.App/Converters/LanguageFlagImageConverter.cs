using System;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using IconifyBundle;

namespace SkyCD.App.Converters;

public sealed class LanguageFlagImageConverter : IValueConverter
{
    private static readonly Lazy<DrawingImage> EnglishFlag = new(() => Load(Twemoji.FlagUnitedKingdom));
    private static readonly Lazy<DrawingImage> LithuanianFlag = new(() => Load(Twemoji.FlagLithuania));
    private static readonly Lazy<DrawingImage> Globe = new(() => Load(Twemoji.GlobeWithMeridians));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ResolveAssetName(value) switch
        {
            "flag-united-kingdom" => EnglishFlag.Value,
            "flag-lithuania" => LithuanianFlag.Value,
            _ => Globe.Value
        };

    public static string ResolveAssetName(object? flag) => flag switch
    {
        "🇬🇧" => "flag-united-kingdom",
        "🇱🇹" => "flag-lithuania",
        _ => "globe-with-meridians"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static DrawingImage Load(Icon icon)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = new RectangleGeometry(new Rect(0, 0, icon.Width, icon.Height))
        });

        // These pinned Twemoji assets consist entirely of paths with solid fills.
        foreach (var path in XElement.Parse(icon.Svg).Elements())
        {
            if (path.Name.LocalName != "path" ||
                path.Attribute("d") is not { Value: var data } ||
                path.Attribute("fill") is not { Value: var fill })
            {
                throw new FormatException($"Twemoji icon '{icon.Name}' must contain only solid-filled paths.");
            }

            drawing.Children.Add(new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.Parse(fill)),
                Geometry = StreamGeometry.Parse("F1 " + data)
            });
        }

        return new DrawingImage { Drawing = drawing };
    }
}
