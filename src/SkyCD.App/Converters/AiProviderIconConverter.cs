using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using SkyCD.Resource.Icons.LlmBackends;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.App.Converters;

/// <summary>Loads a provider preset's embedded icon. A missing resource returns null so the monogram shows.</summary>
public sealed class AiProviderIconConverter : IValueConverter
{
    private const string PackagedIconPrefix = "llm-backend:";

    public static AiProviderIconConverter Instance { get; } = new();

    private readonly ConcurrentDictionary<string, Bitmap?> cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AiProviderChoice { IconKey: { Length: > 0 } key } ||
            !key.StartsWith(PackagedIconPrefix, StringComparison.Ordinal)) return null;
        return cache.GetOrAdd(key[PackagedIconPrefix.Length..], Load);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Bitmap? Load(string providerId)
    {
        try
        {
            using var resource = BackendIcons.OpenPng(providerId, 16);
            if (resource is null) return null;
            return new Bitmap(resource);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException
                                           or BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
