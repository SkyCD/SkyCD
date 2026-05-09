using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SkyCD.Plugin.Runtime.Managers;

public sealed class FileFormatFilterCollection(IReadOnlyList<FileFormatFilterDescriptor> items)
    : ReadOnlyCollection<FileFormatFilterDescriptor>(PrepareItems(items))
{
    private static IList<FileFormatFilterDescriptor> PrepareItems(IReadOnlyList<FileFormatFilterDescriptor> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items as IList<FileFormatFilterDescriptor> ?? items.ToList();
    }
}
