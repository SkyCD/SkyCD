using System.Collections.Generic;
using SkyCD.Plugin.Fonts.Models;

namespace SkyCD.Plugin.Fonts.Configuration;

/// <summary>
/// Editable state of the font configuration surface. Values are normalized through <see cref="FontSettings"/>
/// when the surface is accepted, so the persisted dictionary can never contain a size, limit or extension the
/// extraction code would reject.
/// </summary>
public sealed class FontConfigurationEditor
{
    public FontConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        var current = FontSettings.From(currentSettings);
        CollectionEnabled = current.CollectionEnabled;
        PreviewsEnabled = current.PreviewsEnabled;
        PreviewText = current.PreviewText;
        PreviewSize = current.PreviewSize;
        PreviewLimitPerFolder = current.PreviewLimitPerFolder;
        Extensions = string.Join(';', current.Extensions);
    }

    public bool CollectionEnabled { get; set; }

    public bool PreviewsEnabled { get; set; }

    public string PreviewText { get; set; }

    public int PreviewSize { get; set; }

    public int PreviewLimitPerFolder { get; set; }

    /// <summary>Semicolon-separated extensions as typed; parsed and normalized on acceptance.</summary>
    public string Extensions { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings() => new FontSettings(
        CollectionEnabled,
        PreviewsEnabled,
        PreviewText,
        PreviewSize,
        PreviewLimitPerFolder,
        FontSettings.ParseExtensions(Extensions)).ToDictionary();
}
