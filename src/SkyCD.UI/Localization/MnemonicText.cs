namespace SkyCD.UI.Localization;

/// <summary>
/// Removes access-key markers (underscore and ampersand) from localized display text
/// when the text is rendered outside of a menu or another control that interprets them.
/// </summary>
public static class MnemonicText
{
    /// <summary>
    /// Returns <paramref name="text"/> with every mnemonic marker removed,
    /// matching how the toolbar already presents localized labels.
    /// Null or empty input yields an empty string.
    /// </summary>
    public static string Strip(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("_", string.Empty).Replace("&", string.Empty);
    }
}
