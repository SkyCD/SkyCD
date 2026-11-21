using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkyCD.Plugin.Fonts.Models;

/// <summary>
/// User-facing options of the font plugin, persisted by the host's plugin configuration store and pushed
/// into the capabilities through <c>IPluginSettingsConsumer</c>. Every option has a documented default, so a
/// freshly installed plugin behaves predictably without opening the configuration surface.
/// </summary>
public sealed class FontSettings
{
    public const string CollectionEnabledKey = "collectionEnabled";
    public const string PreviewsEnabledKey = "previewsEnabled";
    public const string PreviewTextKey = "previewText";
    public const string PreviewSizeKey = "previewSize";
    public const string PreviewLimitPerFolderKey = "previewLimitPerFolder";
    public const string ExtensionsKey = "extensions";

    public const int MinPreviewSize = 8;
    public const int MaxPreviewSize = 200;
    public const int DefaultPreviewSize = 60;
    public const int MaxPreviewLimitPerFolder = 1000;
    public const int MaxPreviewTextLength = 200;
    public const int MaxExtensionCount = 32;

    public const string DefaultPreviewText = "The quick brown fox jumps over the lazy dog";

    private static readonly char[] ExtensionSeparators = [';', ',', ' '];

    public FontSettings(
        bool collectionEnabled,
        bool previewsEnabled,
        string previewText,
        int previewSize,
        int previewLimitPerFolder,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        CollectionEnabled = collectionEnabled;
        PreviewsEnabled = previewsEnabled;
        PreviewText = NormalizePreviewText(previewText);
        PreviewSize = Math.Clamp(previewSize, MinPreviewSize, MaxPreviewSize);
        PreviewLimitPerFolder = Math.Clamp(previewLimitPerFolder, 0, MaxPreviewLimitPerFolder);
        Extensions = extensions.Count > 0 ? extensions : FontSchema.DefaultExtensions;
    }

    public bool CollectionEnabled { get; }

    public bool PreviewsEnabled { get; }

    /// <summary>Text drawn into the preview; line breaks are kept, blank input falls back to the default.</summary>
    public string PreviewText { get; }

    /// <summary>Pixel size of the preview text, from 8 to 200.</summary>
    public int PreviewSize { get; }

    /// <summary>Previews generated per folder; zero means no limit.</summary>
    public int PreviewLimitPerFolder { get; }

    /// <summary>Extensions the plugin claims, each with a leading dot, in configuration order.</summary>
    public IReadOnlyList<string> Extensions { get; }

    public static FontSettings Default { get; } = new(
        collectionEnabled: true,
        previewsEnabled: false,
        previewText: DefaultPreviewText,
        previewSize: DefaultPreviewSize,
        previewLimitPerFolder: 0,
        extensions: FontSchema.DefaultExtensions);

    /// <summary>
    /// Reads persisted settings, falling back to defaults per key. Unreadable values are ignored instead of
    /// failing a scan: a corrupted option means "keep the documented default", not "stop indexing".
    /// </summary>
    public static FontSettings From(IReadOnlyDictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return Default;
        }

        return new FontSettings(
            ReadBoolean(settings, CollectionEnabledKey, Default.CollectionEnabled),
            ReadBoolean(settings, PreviewsEnabledKey, Default.PreviewsEnabled),
            Read(settings, PreviewTextKey) ?? Default.PreviewText,
            ReadInteger(settings, PreviewSizeKey, Default.PreviewSize, MinPreviewSize, MaxPreviewSize),
            ReadInteger(settings, PreviewLimitPerFolderKey, Default.PreviewLimitPerFolder, 0, MaxPreviewLimitPerFolder),
            ParseExtensions(Read(settings, ExtensionsKey)));
    }

    public IReadOnlyDictionary<string, string?> ToDictionary() => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        [CollectionEnabledKey] = CollectionEnabled ? "true" : "false",
        [PreviewsEnabledKey] = PreviewsEnabled ? "true" : "false",
        [PreviewTextKey] = PreviewText,
        [PreviewSizeKey] = PreviewSize.ToString(CultureInfo.InvariantCulture),
        [PreviewLimitPerFolderKey] = PreviewLimitPerFolder.ToString(CultureInfo.InvariantCulture),
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
            return FontSchema.DefaultExtensions;
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

        return parsed.Count > 0 ? parsed : FontSchema.DefaultExtensions;
    }

    private static string NormalizePreviewText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPreviewText;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        return normalized.Length > MaxPreviewTextLength ? normalized[..MaxPreviewTextLength] : normalized;
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
        int.TryParse(Read(settings, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}
