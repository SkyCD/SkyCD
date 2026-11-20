using System.Collections.Generic;

namespace SkyCD.Plugin.EComic;

/// <summary>
/// Editable state of the eComic configuration surface. Values are normalized through
/// <see cref="EComicSettings"/> when the surface is accepted, so the persisted dictionary can never contain a
/// cover size or extension list the extraction code would reject.
/// </summary>
public sealed class EComicConfigurationEditor
{
    public EComicConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        var current = EComicSettings.From(currentSettings);
        CollectionEnabled = current.CollectionEnabled;
        NameSource = current.NameSource;
        TagWriters = current.TagWriters;
        ExtractCovers = current.ExtractCovers;
        CoverSize = current.CoverSize;
        Extensions = string.Join(';', current.Extensions);
    }

    public bool CollectionEnabled { get; set; }

    public EComicNameSource NameSource { get; set; }

    public bool TagWriters { get; set; }

    public bool ExtractCovers { get; set; }

    public int CoverSize { get; set; }

    /// <summary>Semicolon-separated extensions as typed; parsed and normalized on acceptance.</summary>
    public string Extensions { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings() => new EComicSettings(
        CollectionEnabled,
        NameSource,
        TagWriters,
        ExtractCovers,
        CoverSize,
        EComicSettings.ParseExtensions(Extensions)).ToDictionary();

    /// <summary>Stable order of the name-source choices shown in the configuration surface.</summary>
    public static IReadOnlyList<EComicNameSource> NameSources { get; } =
        [EComicNameSource.FileName, EComicNameSource.Series, EComicNameSource.Title];

    public static string DescribeNameSource(EComicNameSource source) => source switch
    {
        EComicNameSource.Series => "Series (fall back to file name)",
        EComicNameSource.Title => "Title (fall back to file name)",
        _ => "File name"
    };
}
