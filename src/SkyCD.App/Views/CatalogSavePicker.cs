using System;
using System.Linq;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.App.Views;

/// <summary>
/// Builds the Save/Save As picker from the file-format manager the readiness guard returns, so the
/// dialog never lists the empty host-container manager held before the plugin runtime is synchronised.
/// </summary>
internal static class CatalogSavePicker
{
    public static FilePickerSaveOptions? Create(
        Func<FileFormatManager> ensureFileFormatManager,
        bool saveAs,
        string title,
        string? allFilesLabel)
    {
        var manager = ensureFileFormatManager();
        var saveFormats = manager.GetSaveFormats();
        if (saveFormats.Count == 0)
        {
            return null;
        }

        var defaultExtension = saveFormats.Any(format => format.FormatId == "skycd-json")
            ? "json"
            : manager.GetPreferredSaveExtension();

        return new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = $"catalog.{defaultExtension}",
            DefaultExtension = defaultExtension,
            FileTypeChoices = manager.GetSaveFilters()
                .ToFilePickerTypes(allSupportedFilesLabel: null, allFilesLabel: allFilesLabel)
        };
    }
}
