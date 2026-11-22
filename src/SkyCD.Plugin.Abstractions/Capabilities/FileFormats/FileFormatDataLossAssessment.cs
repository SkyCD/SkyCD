using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Result of a preflight loss assessment.
/// </summary>
/// <param name="Items">Data groups the target format cannot represent.</param>
public sealed record FileFormatDataLossAssessment(IReadOnlyList<FileFormatDataLossItem> Items)
{
    /// <summary>Gets an assessment with no loss.</summary>
    public static FileFormatDataLossAssessment Lossless { get; } = new([]);

    /// <summary>Gets whether any catalog data would be lost.</summary>
    public bool HasLoss => Items.Count > 0;
}
