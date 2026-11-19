using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SkyCD.App.Converters;

public sealed class LanguageFlagImageConverter : IValueConverter
{
    private static readonly Lazy<Bitmap> EnglishFlag = new(() => Load("GB"));
    private static readonly Lazy<Bitmap> LithuanianFlag = new(() => Load("LT"));
    private static readonly Lazy<Bitmap> Globe = new(() => Load("globe"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ResolveAssetName(value) switch
        {
            "GB" => EnglishFlag.Value,
            "LT" => LithuanianFlag.Value,
            _ => Globe.Value
        };

    public static string ResolveAssetName(object? flag) => flag switch
    {
        "🇬🇧" => "GB",
        "🇱🇹" => "LT",
        _ => "globe"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Bitmap Load(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://SkyCD.App/Assets/LanguageFlags/{name}.png"));
        return new Bitmap(stream);
    }
}
