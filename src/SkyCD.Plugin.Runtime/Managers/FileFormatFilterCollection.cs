using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Platform.Storage;

namespace SkyCD.Plugin.Runtime.Managers;

public sealed class FileFormatFilterCollection(IReadOnlyList<FileFormatFilterDescriptor> items)
    : ReadOnlyCollection<FileFormatFilterDescriptor>(PrepareItems(items))
{
    public List<FilePickerFileType> ToFilePickerTypes(
        string? allSupportedFilesLabel = null,
        string? allFilesLabel = null)
    {
        var pickerTypes = this
            .Select(filter => new FilePickerFileType(filter.DisplayName)
            {
                Patterns = filter.Patterns.ToArray()
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(allSupportedFilesLabel))
        {
            var supportedPatterns = this
                .SelectMany(static filter => filter.Patterns)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (supportedPatterns.Length > 0)
            {
                pickerTypes.Insert(0, new FilePickerFileType(allSupportedFilesLabel)
                {
                    Patterns = supportedPatterns
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(allFilesLabel))
        {
            pickerTypes.Add(new FilePickerFileType(allFilesLabel)
            {
                Patterns = ["*.*"]
            });
        }

        return pickerTypes;
    }

    private static IList<FileFormatFilterDescriptor> PrepareItems(IReadOnlyList<FileFormatFilterDescriptor> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items as IList<FileFormatFilterDescriptor> ?? items.ToList();
    }
}
