using System;
using System.Text;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>Turns project names into single-line display text and caps their length.</summary>
internal static class ProjectText
{
    /// <summary>Trims padding and collapses whitespace; returns null for a blank name.</summary>
    public static string? Clean(string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = new StringBuilder(Math.Min(value.Length, maxCharacters));
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character == '\0' || char.IsWhiteSpace(character))
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }

            if (text.Length == maxCharacters)
            {
                break;
            }

            text.Append(character);
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
