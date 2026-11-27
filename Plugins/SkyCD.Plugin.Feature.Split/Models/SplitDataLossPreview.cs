using System.Collections.Generic;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>What the chosen saving format would drop from the catalog, for the one confirmation per run.</summary>
/// <param name="FormatDisplayName">The saving format's display name, shown in the question.</param>
/// <param name="Items">The catalog data the format cannot store.</param>
public sealed record SplitDataLossPreview(
    string FormatDisplayName,
    IReadOnlyList<FileFormatDataLossItem> Items);
