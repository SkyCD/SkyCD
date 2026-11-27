using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.Plugin.Feature.Split.ViewModels;

/// <summary>A saving format the split dialog offers. Only formats that can be written and read back are
/// offered, so every part reopens as a valid catalog.</summary>
/// <param name="FormatId">The format the parts are written in.</param>
/// <param name="DisplayName">The name shown in the format picker.</param>
/// <param name="DefaultExtension">The extension new part file names default to, without a leading dot.</param>
/// <param name="Extensions">Every extension the format writes, with or without a leading dot.</param>
public sealed record SplitFormatOption(
    string FormatId,
    string DisplayName,
    string DefaultExtension,
    IReadOnlyList<string> Extensions)
{
    /// <summary>Projects a host format descriptor into the picker option.</summary>
    public static SplitFormatOption FromDescriptor(FileFormatDescriptor format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new SplitFormatOption(
            format.FormatId,
            format.DisplayName,
            format.Extensions.Select(static extension => extension.Trim().TrimStart('.'))
                .FirstOrDefault(static extension => !string.IsNullOrWhiteSpace(extension)) ?? "dat",
            format.Extensions.ToArray());
    }
}
