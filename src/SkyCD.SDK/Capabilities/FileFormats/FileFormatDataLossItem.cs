namespace SkyCD.SDK.Capabilities.FileFormats;

/// <summary>
/// One group of catalog data a target format cannot represent.
/// </summary>
/// <param name="Category">Stable category identifier, see <see cref="FileFormatDataLossCategories"/>.</param>
/// <param name="Name">Item identifier inside the category, for example <c>status</c> or <c>property:Artist</c>.</param>
/// <param name="AffectedCount">Number of affected entries or catalog records.</param>
/// <param name="Sample">Optional short sample of affected names.</param>
public sealed record FileFormatDataLossItem(
    string Category,
    string Name,
    int AffectedCount,
    string? Sample = null);
