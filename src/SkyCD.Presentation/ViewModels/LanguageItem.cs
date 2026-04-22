namespace SkyCD.Presentation.ViewModels;

/// <summary>
///     Represents a language option with display name and flag emoji.
/// </summary>
public sealed record LanguageItem(string Name, string Flag)
{
    /// <summary>
    ///     Gets the display text combining flag and language name.
    /// </summary>
    public string DisplayText => $"{Flag} {Name}";

    /// <summary>
    ///     Gets common language flag mappings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FlagMappings =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "English", "🇬🇧" },
            { "Lithuanian", "🇱🇹" },
            { "Russian", "🇷🇺" },
            { "Polish", "🇵🇱" },
            { "German", "🇩🇪" },
            { "French", "🇫🇷" },
            { "Spanish", "🇪🇸" },
            { "Italian", "🇮🇹" },
            { "Portuguese", "🇵🇹" },
            { "Dutch", "🇳🇱" },
            { "Swedish", "🇸🇪" },
            { "Norwegian", "🇳🇴" },
            { "Danish", "🇩🇰" },
            { "Finnish", "🇫🇮" },
            { "Greek", "🇬🇷" },
            { "Czech", "🇨🇿" },
            { "Slovak", "🇸🇰" },
            { "Hungarian", "🇭🇺" },
            { "Romanian", "🇷🇴" },
            { "Bulgarian", "🇧🇬" },
            { "Croatian", "🇭🇷" },
            { "Serbian", "🇷🇸" },
            { "Ukrainian", "🇺🇦" },
            { "Turkish", "🇹🇷" },
            { "Japanese", "🇯🇵" },
            { "Chinese", "🇨🇳" },
            { "Korean", "🇰🇷" }
        };

    /// <summary>
    ///     Creates a LanguageItem from a language name, auto-detecting the flag.
    /// </summary>
    public static LanguageItem Create(string languageName)
    {
        var flag = ResolveFlag(languageName);
        return new LanguageItem(languageName, flag);
    }

    private static string ResolveFlag(string languageName)
    {
        if (FlagMappings.TryGetValue(languageName, out var directMatch)) return directMatch;

        var normalized = (languageName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "🌐";

        var bracketIndex = normalized.IndexOf('(');
        if (bracketIndex > 0)
        {
            var baseName = normalized[..bracketIndex].Trim();
            if (FlagMappings.TryGetValue(baseName, out var baseNameMatch)) return baseNameMatch;
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            var baseName = normalized[..dashIndex].Trim();
            if (FlagMappings.TryGetValue(baseName, out var dashedMatch)) return dashedMatch;
        }

        return "🌐";
    }
}