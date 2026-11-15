using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public sealed record CatalogFindResult(CatalogDocument Document, string FullPath, string Context)
{
    /// <summary>
    /// Label of the contributed field that produced the match, or null when the catalog path matched.
    /// </summary>
    public string? MatchedField { get; init; }

    /// <summary>Drives visibility of the matched-field column without a converter.</summary>
    public bool HasMatchedField => !string.IsNullOrEmpty(MatchedField);
}
