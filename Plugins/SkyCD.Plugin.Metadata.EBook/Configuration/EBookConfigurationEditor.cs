using System.Collections.Generic;
using SkyCD.Plugin.Metadata.EBook.Models;

namespace SkyCD.Plugin.Metadata.EBook.Configuration;

/// <summary>
/// Editable state of the eBook configuration surface. Values are normalized through
/// <see cref="EBookSettings"/> when the surface is accepted, so the persisted dictionary can never contain a
/// cover size or extension list the extraction code would reject.
/// </summary>
public sealed class EBookConfigurationEditor
{
    public EBookConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        var current = EBookSettings.From(currentSettings);
        CollectionEnabled = current.CollectionEnabled;
        NameSource = current.NameSource;
        TagAuthors = current.TagAuthors;
        ExtractCovers = current.ExtractCovers;
        CoverSize = current.CoverSize;
        Extensions = string.Join(';', current.Extensions);
    }

    public bool CollectionEnabled { get; set; }

    public EBookNameSource NameSource { get; set; }

    public bool TagAuthors { get; set; }

    public bool ExtractCovers { get; set; }

    public int CoverSize { get; set; }

    /// <summary>Semicolon-separated extensions as typed; parsed and normalized on acceptance.</summary>
    public string Extensions { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings() => new EBookSettings(
        CollectionEnabled,
        NameSource,
        TagAuthors,
        ExtractCovers,
        CoverSize,
        EBookSettings.ParseExtensions(Extensions)).ToDictionary();

    /// <summary>Stable order of the name-source choices shown in the configuration surface.</summary>
    public static IReadOnlyList<EBookNameSource> NameSources { get; } =
        [EBookNameSource.FileName, EBookNameSource.Author, EBookNameSource.Title];

    public static string DescribeNameSource(EBookNameSource source) => source switch
    {
        EBookNameSource.Author => "Author (fall back to file name)",
        EBookNameSource.Title => "Title (fall back to file name)",
        _ => "File name"
    };
}
