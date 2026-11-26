using System.Collections.Generic;

namespace SkyCD.Presentation.Catalog.Split;

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
    IReadOnlyList<string> Extensions);
