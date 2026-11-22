using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.EBook.Models;

/// <summary>Which captured value may stand in for the file name in the catalog browser.</summary>
public enum EBookNameSource
{
    FileName,
    Author,
    Title
}

/// <summary>
/// User-facing options of the eBook plugin, persisted by the host's plugin configuration store and pushed
/// into the extraction capabilities through <c>IPluginSettingsConsumer</c>. Every option has a default that
/// keeps a freshly installed plugin behaving predictably without opening the configuration surface.
/// </summary>
public sealed class EBookSettings
{
    public const string CollectionEnabledKey = "collectionEnabled";
    public const string NameSourceKey = "nameSource";
    public const string TagAuthorsKey = "tagAuthors";
    public const string ExtractCoversKey = "extractCovers";
    public const string CoverSizeKey = "coverSize";
    public const string ExtensionsKey = "extensions";

    public const int MinCoverSize = 64;
    public const int MaxCoverSize = 2048;
    public const int DefaultCoverSize = 250;
    public const int MaxExtensionCount = 32;

    private static readonly char[] ExtensionSeparators = [';', ',', ' '];

    public EBookSettings(
        bool collectionEnabled,
        EBookNameSource nameSource,
        bool tagAuthors,
        bool extractCovers,
        int coverSize,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        CollectionEnabled = collectionEnabled;
        NameSource = nameSource is EBookNameSource.Author or EBookNameSource.Title
            ? nameSource
            : EBookNameSource.FileName;
        TagAuthors = tagAuthors;
        ExtractCovers = extractCovers;
        CoverSize = Math.Clamp(coverSize, MinCoverSize, MaxCoverSize);
        Extensions = extensions.Count > 0 ? extensions : EBookSchema.DefaultExtensions;
    }

    public bool CollectionEnabled { get; }

    public EBookNameSource NameSource { get; }

    public bool TagAuthors { get; }

    public bool ExtractCovers { get; }

    /// <summary>Longest edge of the extracted cover; larger images are scaled down, never up.</summary>
    public int CoverSize { get; }

    /// <summary>Extensions the plugin claims, each with a leading dot, in configuration order.</summary>
    public IReadOnlyList<string> Extensions { get; }

    public static EBookSettings Default { get; } = new(
        collectionEnabled: true,
        nameSource: EBookNameSource.FileName,
        tagAuthors: false,
        extractCovers: false,
        coverSize: DefaultCoverSize,
        extensions: EBookSchema.DefaultExtensions);

    /// <summary>
    /// Reads persisted settings, falling back to defaults per key. Unreadable values are ignored instead of
    /// failing a scan: a corrupted option means "keep the documented default", not "stop indexing".
    /// </summary>
    public static EBookSettings From(IReadOnlyDictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return Default;
        }

        return new EBookSettings(
            ReadBoolean(settings, CollectionEnabledKey, Default.CollectionEnabled),
            ReadNameSource(settings),
            ReadBoolean(settings, TagAuthorsKey, Default.TagAuthors),
            ReadBoolean(settings, ExtractCoversKey, Default.ExtractCovers),
            ReadInteger(settings, CoverSizeKey, Default.CoverSize, MinCoverSize, MaxCoverSize),
            ParseExtensions(Read(settings, ExtensionsKey)));
    }

    public IReadOnlyDictionary<string, string?> ToDictionary() => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        [CollectionEnabledKey] = CollectionEnabled ? "true" : "false",
        [NameSourceKey] = NameSource switch
        {
            EBookNameSource.Author => "author",
            EBookNameSource.Title => "title",
            _ => "filename"
        },
        [TagAuthorsKey] = TagAuthors ? "true" : "false",
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
            return EBookSchema.DefaultExtensions;
        }

        var parsed = new List<string>();
        foreach (var candidate in value.Split(ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = (candidate.StartsWith('.') ? candidate : '.' + candidate).ToLowerInvariant();
            if (extension.Length < 2 || extension.Contains('*') || extension.Contains('\\') || extension.Contains('/'))
            {
                continue;
            }

            if (!parsed.Contains(extension))
            {
                parsed.Add(extension);
            }

            if (parsed.Count == MaxExtensionCount)
            {
                break;
            }
        }

        return parsed.Count > 0 ? parsed : EBookSchema.DefaultExtensions;
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

    private static EBookNameSource ReadNameSource(IReadOnlyDictionary<string, string?> settings)
    {
        var value = Read(settings, NameSourceKey);
        if (string.Equals(value, "author", StringComparison.OrdinalIgnoreCase))
        {
            return EBookNameSource.Author;
        }

        return string.Equals(value, "title", StringComparison.OrdinalIgnoreCase)
            ? EBookNameSource.Title
            : EBookNameSource.FileName;
    }
}
