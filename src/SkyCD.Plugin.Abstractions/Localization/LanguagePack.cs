namespace SkyCD.Plugin.Abstractions.Localization;

/// <summary>A discovered UI language and its optional display metadata.</summary>
public sealed record LanguagePack(string CultureCode, string DisplayName, string Flag = "🌐");
