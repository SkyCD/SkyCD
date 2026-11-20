using System.Collections.Generic;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Editable state of the options surface. Values are normalized through <see cref="FileMetadataSettings"/>
/// when the surface is accepted, so the persisted dictionary can never contain an extension the extraction
/// code would reject.
/// </summary>
public sealed class FileMetadataConfigurationEditor
{
    public FileMetadataConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        var current = FileMetadataSettings.From(currentSettings);
        CollectionEnabled = current.CollectionEnabled;
        KeywordsEnabled = current.KeywordsEnabled;
        SplitHierarchicalTags = current.SplitHierarchicalTags;
        CommentsEnabled = current.CommentsEnabled;
        RatingsEnabled = current.RatingsEnabled;
        Extensions = string.Join(';', current.Extensions);
    }

    public bool CollectionEnabled { get; set; }

    public bool KeywordsEnabled { get; set; }

    public bool SplitHierarchicalTags { get; set; }

    public bool CommentsEnabled { get; set; }

    public bool RatingsEnabled { get; set; }

    /// <summary>Semicolon-separated extensions as typed; parsed and normalized on acceptance.</summary>
    public string Extensions { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings() => new FileMetadataSettings(
        CollectionEnabled,
        KeywordsEnabled,
        SplitHierarchicalTags,
        CommentsEnabled,
        RatingsEnabled,
        FileMetadataSettings.ParseExtensions(Extensions)).ToDictionary();
}
