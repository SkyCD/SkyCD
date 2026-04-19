namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Represents a language option with display name and flag emoji.
/// </summary>
public sealed record LanguageItem(string Name, string Flag)
{
    /// <summary>
    /// Gets the display text combining flag and language name.
    /// </summary>
    public string DisplayText => $"{Flag} {Name}";

    /// <summary>
    /// Gets common language flag mappings.
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
            { "Korean", "🇰🇷" },
        };

    /// <summary>
    /// Creates a LanguageItem from a language name, auto-detecting the flag.
    /// </summary>
    public static LanguageItem Create(string languageName)
    {
        var flag = FlagMappings.TryGetValue(languageName, out var f) ? f : "🌐";
        return new LanguageItem(languageName, flag);
    }
}
