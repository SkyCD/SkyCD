using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using IconifyBundle;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

/// <summary>
/// Paints the language flags of the UI as colored Twemoji artwork, the same solid-fill pipeline the toolbar
/// icons use (issue #1022).
/// </summary>
public sealed class LanguageFlagImageConverter : IValueConverter
{
    private static readonly Lazy<DrawingImage> EnglishFlag = new(() => TwemojiArtwork.Create(Twemoji.FlagUnitedKingdom));
    private static readonly Lazy<DrawingImage> LithuanianFlag = new(() => TwemojiArtwork.Create(Twemoji.FlagLithuania));
    private static readonly Lazy<DrawingImage> Globe = new(() => TwemojiArtwork.Create(Twemoji.GlobeWithMeridians));

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
}
