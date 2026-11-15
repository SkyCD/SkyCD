namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One searchable field of a catalog entry contributed by an <see cref="ICatalogSearchFieldProvider"/>.
/// Values are already rendered for display, so matching them needs no format-specific knowledge.
/// </summary>
public sealed record CatalogSearchField(string Key, string Value);
