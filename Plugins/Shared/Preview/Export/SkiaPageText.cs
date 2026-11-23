using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Preview.Export;

/// <summary>Token helpers shared by the page renderers.</summary>
/// <remarks>
/// The page draws tokens one at a time and decides about wrapping on whitespace, so every plugin has to split
/// its text the same way; keeping the split here means a line break or a run of spaces measures and wraps
/// identically in every format.
/// </remarks>
internal static class SkiaPageText
{
    /// <summary><c>true</c> when the token is non-empty and holds only whitespace.</summary>
    internal static bool IsWhitespace(string token)
    {
        foreach (var ch in token)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    /// <summary>
    /// Splits text into a separate <c>"\n"</c> token per line break and groups the rest into runs of
    /// whitespace or non-whitespace. Carriage returns are left untouched; callers that normalise them do so
    /// before tokenising.
    /// </summary>
    internal static List<string> TokenizePreservingNewlines(string text)
    {
        var tokens = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '\n')
            {
                tokens.Add("\n");
                index++;
                continue;
            }

            var start = index;
            var whitespace = char.IsWhiteSpace(text[index]);
            while (index < text.Length && text[index] != '\n' &&
                   char.IsWhiteSpace(text[index]) == whitespace)
            {
                index++;
            }

            tokens.Add(text[start..index]);
        }

        return tokens;
    }
}
