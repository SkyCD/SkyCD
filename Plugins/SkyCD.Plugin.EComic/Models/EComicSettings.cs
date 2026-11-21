using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.EComic.Models;

/// <summary>Which captured value may stand in for the file name in the catalog browser.</summary>
public enum EComicNameSource
{
    FileName,
    Series,
    Title
}

/// <summary>
/// User-facing options of the eComic plugin, persisted by the host's plugin configuration store and pushed
/// into the extraction capabilities through <c>IPluginSettingsConsumer</c>. Every option has a default that
/// keeps a freshly installed plugin behaving predictably without opening the configuration surface.
/// </summary>
public sealed class EComicSettings
{
    public const string CollectionEnabledKey = "collectionEnabled";
    public const string NameSourceKey = "nameSource";
    public const string TagWritersKey = "tagWriters";
    public const string ExtractCoversKey = "extractCovers";
    public const string CoverSizeKey = "coverSize";
    public const string ExtensionsKey = "extensions";

    public const int MinCoverSize = 64;
    public const int MaxCoverSize = 2048;
    public const int DefaultCoverSize = 720;
    public const int MaxExtensionCount = 32;

    private static readonly char[] ExtensionSeparators = [';', ',', ' '];

    public EComicSettings(
        bool collectionEnabled,
        EComicNameSource nameSource,
        bool tagWriters,
        bool extractCovers,
        int coverSize,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        CollectionEnabled = collectionEnabled;
        NameSource = nameSource is EComicNameSource.Series or EComicNameSource.Title
            ? nameSource
            : EComicNameSource.FileName;
        TagWriters = tagWriters;
        ExtractCovers = extractCovers;
        CoverSize = Math.Clamp(coverSize, MinCoverSize, MaxCoverSize);
        Extensions = extensions.Count > 0 ? extensions : EComicSchema.DefaultExtensions;
    }

    public bool CollectionEnabled { get; }

    public EComicNameSource NameSource { get; }

    public bool TagWriters { get; }

    public bool ExtractCovers { get; }

    /// <summary>Longest edge of the extracted cover; larger pages are scaled down, never up.</summary>
    public int CoverSize { get; }

    /// <summary>Extensions the plugin claims, each with a leading dot, in configuration order.</summary>
    public IReadOnlyList<string> Extensions { get; }

    public static EComicSettings Default { get; } = new(
        collectionEnabled: true,
        nameSource: EComicNameSource.FileName,
        tagWriters: false,
        extractCovers: false,
        coverSize: DefaultCoverSize,
        extensions: EComicSchema.DefaultExtensions);

    /// <summary>
    /// Reads persisted settings, falling back to defaults per key. Unreadable values are ignored instead of
    /// failing a scan: a corrupted option means "keep the documented default", not "stop indexing".
    /// </summary>
    public static EComicSettings From(IReadOnlyDictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return Default;
        }

        return new EComicSettings(
            ReadBoolean(settings, CollectionEnabledKey, Default.CollectionEnabled),
            ReadNameSource(settings),
            ReadBoolean(settings, TagWritersKey, Default.TagWriters),
            ReadBoolean(settings, ExtractCoversKey, Default.ExtractCovers),
            ReadInteger(settings, CoverSizeKey, Default.CoverSize, MinCoverSize, MaxCoverSize),
            ParseExtensions(Read(settings, ExtensionsKey)));
    }

    public IReadOnlyDictionary<string, string?> ToDictionary() => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        [CollectionEnabledKey] = CollectionEnabled ? "true" : "false",
        [NameSourceKey] = NameSource switch
        {
            EComicNameSource.Series => "series",
            EComicNameSource.Title => "title",
            _ => "filename"
        },
        [TagWritersKey] = TagWriters ? "true" : "false",
        [ExtractCoversKey] = ExtractCovers ? "true" : "false",
        [CoverSizeKey] = CoverSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [ExtensionsKey] = string.Join(';', Extensions)
    };

    /// <summary>
    /// Splits the configured list into normalized extensions. Entries without a leading dot gain one, duplicates
    /// disappear, and the list is capped so a pathological value cannot turn matching into a linear slog.
    /// </summary>
    public static IReadOnlyList<string> ParseExtensions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EComicSchema.DefaultExtensions;
        }

        var parsed = new List<string>();
        foreach (var candidate in value.Split(ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = candidate.StartsWith('.') ? candidate : '.' + candidate;
            if (extension.Length < 2 || extension.Contains('*') || extension.Contains('\\') || extension.Contains('/'))
            {
                continue;
            }

            if (!parsed.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                parsed.Add(extension.ToLowerInvariant());
            }

            if (parsed.Count == MaxExtensionCount)
            {
                break;
            }
        }

        return parsed.Count > 0 ? parsed : EComicSchema.DefaultExtensions;
    }

    private static string? Read(IReadOnlyDictionary<string, string?> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value : null;

    private static bool ReadBoolean(IReadOnlyDictionary<string, string?> settings, string key, bool fallback) =>
        bool.TryParse(Read(settings, key), out var value) ? value : fallback;

    private static int ReadInteger(
        IReadOnlyDictionary<string, string?> settings,
        string key,
        int fallback,
        int minimum,
        int maximum) =>
        int.TryParse(Read(settings, key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static EComicNameSource ReadNameSource(IReadOnlyDictionary<string, string?> settings)
    {
        var value = Read(settings, NameSourceKey);
        if (string.Equals(value, "series", StringComparison.OrdinalIgnoreCase))
        {
            return EComicNameSource.Series;
        }

        return string.Equals(value, "title", StringComparison.OrdinalIgnoreCase)
            ? EComicNameSource.Title
            : EComicNameSource.FileName;
    }
}
