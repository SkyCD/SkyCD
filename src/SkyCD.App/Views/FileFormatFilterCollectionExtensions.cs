using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.App.Views;

internal static class FileFormatFilterCollectionExtensions
{
    public static IReadOnlyList<FilePickerFileType> ToFilePickerTypes(this FileFormatFilterCollection filters)
    {
        return filters
            .Select(filter => new FilePickerFileType(filter.DisplayName)
            {
                Patterns = filter.Patterns.ToArray()
            })
            .ToArray();
    }
}
