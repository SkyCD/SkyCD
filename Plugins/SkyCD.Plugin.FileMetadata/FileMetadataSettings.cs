using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// User-facing options of the file metadata plugin, persisted by the host's plugin configuration store and
/// pushed into the capability through <c>IPluginSettingsConsumer</c>. Every option has a documented default,
/// so a freshly installed plugin behaves predictably without opening the configuration surface.
/// </summary>
public sealed class FileMetadataSettings
{
    public const string CollectionEnabledKey = "collectionEnabled";
    public const string KeywordsEnabledKey = "keywordsEnabled";
    public const string SplitHierarchicalTagsKey = "splitHierarchicalTags";
    public const string CommentsEnabledKey = "commentsEnabled";
    public const string RatingsEnabledKey = "ratingsEnabled";
    public const string ExtensionsKey = "extensions";

    public const int MaxExtensionCount = 32;

    private static readonly char[] ExtensionSeparators = [';', ',', ' '];

    public FileMetadataSettings(
        bool collectionEnabled,
        bool keywordsEnabled,
        bool splitHierarchicalTags,
        bool commentsEnabled,
        bool ratingsEnabled,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        CollectionEnabled = collectionEnabled;
        KeywordsEnabled = keywordsEnabled;
        SplitHierarchicalTags = splitHierarchicalTags;
        CommentsEnabled = commentsEnabled;
        RatingsEnabled = ratingsEnabled;
        Extensions = extensions.Count > 0 ? extensions : FileMetadataSchema.DefaultExtensions;
    }

    public bool CollectionEnabled { get; }

    /// <summary>Publishes flat keywords and hierarchical tags.</summary>
    public bool KeywordsEnabled { get; }

    /// <summary>
    /// Expands <c>Places|Beach|Sunset</c> into cumulative tags <c>Places</c>, <c>Places / Beach</c> and
    /// <c>Places / Beach / Sunset</c>. When disabled the path is published as one tag with its levels still
    /// separated by <c>|</c>, which is what some catalogs write and expect back.
    /// </summary>
    public bool SplitHierarchicalTags { get; }

    public bool CommentsEnabled { get; }

    public bool RatingsEnabled { get; }

    /// <summary>Extensions the plugin claims, each with a leading dot, in configuration order.</summary>
    public IReadOnlyList<string> Extensions { get; }

    public static FileMetadataSettings Default { get; } = new(
        collectionEnabled: true,
        keywordsEnabled: true,
        splitHierarchicalTags: true,
        commentsEnabled: true,
        ratingsEnabled: true,
        extensions: FileMetadataSchema.DefaultExtensions);

    /// <summary>
    /// Reads persisted settings, falling back to defaults per key. Unreadable values are ignored instead of
    /// failing a scan: a corrupted option means "keep the documented default", not "stop indexing".
    /// </summary>
    public static FileMetadataSettings From(IReadOnlyDictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return Default;
        }

        return new FileMetadataSettings(
            ReadBoolean(settings, CollectionEnabledKey, Default.CollectionEnabled),
            ReadBoolean(settings, KeywordsEnabledKey, Default.KeywordsEnabled),
            ReadBoolean(settings, SplitHierarchicalTagsKey, Default.SplitHierarchicalTags),
            ReadBoolean(settings, CommentsEnabledKey, Default.CommentsEnabled),
            ReadBoolean(settings, RatingsEnabledKey, Default.RatingsEnabled),
            ParseExtensions(Read(settings, ExtensionsKey)));
    }

    public IReadOnlyDictionary<string, string?> ToDictionary() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CollectionEnabledKey] = CollectionEnabled ? "true" : "false",
            [KeywordsEnabledKey] = KeywordsEnabled ? "true" : "false",
            [SplitHierarchicalTagsKey] = SplitHierarchicalTags ? "true" : "false",
            [CommentsEnabledKey] = CommentsEnabled ? "true" : "false",
            [RatingsEnabledKey] = RatingsEnabled ? "true" : "false",
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
            return FileMetadataSchema.DefaultExtensions;
        }

        var parsed = new List<string>();
        foreach (var candidate in value.Split(ExtensionSeparators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

        return parsed.Count > 0 ? parsed : FileMetadataSchema.DefaultExtensions;
    }

    private static string? Read(IReadOnlyDictionary<string, string?> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value : null;

    private static bool ReadBoolean(IReadOnlyDictionary<string, string?> settings, string key, bool fallback) =>
        bool.TryParse(Read(settings, key), out var value) ? value : fallback;
}
