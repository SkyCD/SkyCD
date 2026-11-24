using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using IconifyBundle;

namespace SkyCD.App.Icons;

/// <summary>
/// Renders Twemoji artwork as flat solid-filled geometry (issue #1022). The palette is carried by the
/// artwork itself, so nothing here reads a theme brush and the same result reads on the dark and light
/// themes. Both the language flags and the colored toolbar icons are built through this one pipeline.
/// </summary>
internal static class TwemojiArtwork
{
    // Avalonia picks the fill rule with the leading "F": F1 is non-zero winding, which is the SVG default
    // the Twemoji bodies are authored for.
    private const string FillRulePrefix = "F1 ";

    /// <summary>One painted layer: the artwork's own color and the path data it fills.</summary>
    internal sealed record Layer(string Fill, string PathData);

    /// <summary>The image for an icon, sized to the icon's own canvas.</summary>
    internal static DrawingImage Create(Icon icon) => CreateDrawing(XElement.Parse(icon.Svg), icon.Width, icon.Height, icon.Name);

    /// <summary>
    /// Builds the drawing of an already parsed <c>&lt;svg&gt;</c> document, which lets a test read a shipped
    /// asset from disk instead of relying on the icon registry.
    /// </summary>
    internal static DrawingImage CreateDrawing(XElement svg, double width, double height, string iconName)
    {
        var drawing = new DrawingGroup();

        // The transparent canvas fixes the drawing bounds, so a glyph that does not reach the edge of its
        // slot keeps the relative size the emoji set gives it instead of being scaled up to fill the icon.
        drawing.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = new RectangleGeometry(new Rect(0, 0, width, height))
        });

        foreach (var layer in ReadLayers(svg, iconName))
        {
            drawing.Children.Add(new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.Parse(layer.Fill)),
                Geometry = StreamGeometry.Parse(FillRulePrefix + layer.PathData)
            });
        }

        return new DrawingImage { Drawing = drawing };
    }

    /// <summary>
    /// Reads the painted layers of an icon in document order. A solid <c>path</c>, <c>circle</c> or
    /// <c>ellipse</c> is a layer; anything else, a group or a stroke-driven shape, cannot be painted flat.
    /// </summary>
    internal static IReadOnlyList<Layer> ReadLayers(XElement svg, string iconName)
    {
        var layers = new List<Layer>();
        foreach (var element in svg.Elements())
        {
            var name = element.Name.LocalName;
            if (name is not ("path" or "circle" or "ellipse"))
            {
                throw new UnsupportedTwemojiArtworkException(iconName, name);
            }

            var fill = element.Attribute("fill")?.Value;
            if (string.IsNullOrWhiteSpace(fill) || fill[0] != '#')
            {
                throw new UnsupportedTwemojiArtworkException(iconName, "a shape without a solid color fill");
            }

            layers.Add(new Layer(fill, name switch
            {
                "path" => RequiredAttribute(element, iconName, "d"),
                "circle" => EllipsePathData(element, iconName, "r", "r"),
                _ => EllipsePathData(element, iconName, "rx", "ry")
            }));
        }

        if (layers.Count == 0)
        {
            throw new UnsupportedTwemojiArtworkException(iconName, "no painted shapes");
        }

        return layers;
    }

    private static string EllipsePathData(XElement element, string iconName, string horizontalName, string verticalName)
    {
        var cx = RequiredNumber(element, iconName, "cx");
        var cy = RequiredNumber(element, iconName, "cy");
        var rx = RequiredNumber(element, iconName, horizontalName);
        var ry = RequiredNumber(element, iconName, verticalName);

        // Two half arcs, so the closed figure is a whole ellipse expressed as path data like every layer.
        return FormattableString.Invariant(
            $"M {cx - rx} {cy} a {rx} {ry} 0 1 0 {rx * 2} 0 a {rx} {ry} 0 1 0 {rx * -2} 0 Z");
    }

    private static string RequiredAttribute(XElement element, string iconName, string attributeName) =>
        element.Attribute(attributeName)?.Value is { Length: > 0 } value
            ? value
            : throw new UnsupportedTwemojiArtworkException(iconName, $"'{element.Name.LocalName}' without a '{attributeName}' attribute");

    private static double RequiredNumber(XElement element, string iconName, string attributeName)
    {
        var text = RequiredAttribute(element, iconName, attributeName);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new UnsupportedTwemojiArtworkException(iconName, $"a non numeric '{attributeName}' attribute");
    }
}
