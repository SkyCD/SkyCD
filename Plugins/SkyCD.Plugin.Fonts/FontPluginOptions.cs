using System;

namespace SkyCD.Plugin.Fonts;

/// <summary>
/// Fixed bounds the plugin applies on top of the host's budgets. The host still owns the authoritative
/// source-read and artifact limits; these values stop the plugin from reading or rendering more than it can
/// use, independently of how the user configured the plugin.
/// </summary>
public sealed record FontPluginOptions
{
    public FontPluginOptions(
        long maxFontBytes = 8 * 1024 * 1024,
        int maxNameTableBytes = 1024 * 1024,
        int maxFaces = 64,
        int maxTables = 512,
        int maxFieldCharacters = 512,
        int maxPreviewLines = 6,
        int maxPreviewPixels = 4_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFontBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNameTableBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFaces);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTables);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFieldCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewPixels);

        MaxFontBytes = maxFontBytes;
        MaxNameTableBytes = maxNameTableBytes;
        MaxFaces = maxFaces;
        MaxTables = maxTables;
        MaxFieldCharacters = maxFieldCharacters;
        MaxPreviewLines = maxPreviewLines;
        MaxPreviewPixels = maxPreviewPixels;
    }

    /// <summary>A font larger than this is not read for metadata or previews.</summary>
    public long MaxFontBytes { get; }

    /// <summary>The name table of one face larger than this is ignored rather than parsed.</summary>
    public int MaxNameTableBytes { get; }

    /// <summary>Faces visited in a ttc or otc collection.</summary>
    public int MaxFaces { get; }

    /// <summary>Table records visited per face, so a damaged directory cannot spin the parser.</summary>
    public int MaxTables { get; }

    /// <summary>Longest name value kept, including the copyright.</summary>
    public int MaxFieldCharacters { get; }

    /// <summary>Preview lines kept; extra lines are dropped.</summary>
    public int MaxPreviewLines { get; }

    /// <summary>Pixel cap of the rendered preview, since the decoded canvas is what consumes memory.</summary>
    public int MaxPreviewPixels { get; }

    public static FontPluginOptions Default { get; } = new();
}
