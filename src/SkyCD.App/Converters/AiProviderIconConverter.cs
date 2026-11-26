using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
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
        if (value is not AiProviderChoice { IconKey: { Length: > 0 } key }) return null;
        return cache.GetOrAdd(key, Load);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Bitmap? Load(string key)
    {
        try
        {
            using var resource = key.StartsWith(PackagedIconPrefix, StringComparison.Ordinal)
                ? BackendIcons.OpenPng(key[PackagedIconPrefix.Length..], 16)
                : AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => candidate.GetName().Name == "SkyCD.Plugin.Ai.Provider")
                    ?.GetManifestResourceStream(key);
            if (resource is null) return null;
            using var copy = new MemoryStream();
            resource.CopyTo(copy);
            copy.Position = 0;
            return new Bitmap(copy);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException
                                           or BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
