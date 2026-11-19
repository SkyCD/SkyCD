using System;
using System.Collections.Generic;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Search;

/// <summary>
/// Versioned typed advanced-search query contract. Version 1 covers name/full path,
/// type/extension, size, status, custom properties and extracted metadata with
/// AND/OR groups. Stored values are compared; localized display strings never are.
/// </summary>
public sealed record AdvancedCatalogQuery
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public SkyCD.Presentation.ViewModels.AdvancedFindSortKey SortKey { get; init; }

    public bool SortDescending { get; init; }

    public CatalogQueryScope Scope { get; init; } = new();

    public CatalogQueryGroup Root { get; init; } = new();

    /// <summary>Zero-based offset into the deterministically ordered matches.</summary>
    public int Skip { get; init; }

    /// <summary>Maximum matches to return. Clamped to 1..1000; defaults to 200.</summary>
    public int Take { get; init; } = 200;

    /// <summary>
    /// Optional pre-filter that classifies file entries as duplicates or uniques
    /// before ordinary predicates, sorting, and pagination are applied.
    /// When <see cref="DuplicateFilterMode.None"/> (default), search behavior is unchanged.
    /// </summary>
    public DuplicateFilterMode DuplicateFilter { get; init; } = DuplicateFilterMode.None;

    /// <summary>
    /// Hash algorithm used for duplicate/unique classification. Required when
    /// <see cref="DuplicateFilter"/> is not <see cref="DuplicateFilterMode.None"/>.
    /// </summary>
    public ComparisonAlgorithm HashAlgorithm { get; init; } = ComparisonAlgorithm.None;
}

/// <summary>
/// Narrows evaluation to a subtree and/or a set of entry kinds.
/// Null <see cref="RootId"/> searches the whole catalog; otherwise the root entry
/// itself is included and descendants are included when <see cref="IncludeDescendants"/> is set.
/// </summary>
public sealed record CatalogQueryScope
{
    public string? RootId { get; init; }

    public bool IncludeDescendants { get; init; } = true;

    /// <summary>Null or empty means all <see cref="CatalogDocumentType"/> values.</summary>
    public IReadOnlyList<CatalogDocumentType>? Types { get; init; }
}

/// <summary>AND (<see cref="All"/>) or OR (<see cref="Any"/>) combination of predicates and nested groups.</summary>
public sealed record CatalogQueryGroup
{
    public CatalogQueryCombination Combination { get; init; } = CatalogQueryCombination.All;

    public IReadOnlyList<AdvancedCatalogPredicate> Predicates { get; init; } = [];

    public IReadOnlyList<CatalogQueryGroup> Groups { get; init; } = [];
}

public enum CatalogQueryCombination
{
    All,
    Any
}

/// <summary>
/// Controls the duplicate/unique pre-filter applied before ordinary search predicates.
/// </summary>
public enum DuplicateFilterMode
{
    /// <summary>No duplicate/unique filtering; existing behavior is unchanged.</summary>
    None,

    /// <summary>Return only file entries that belong to a size/hash group of two or more.</summary>
    Duplicates,

    /// <summary>Return only file entries that are singletons among classifiable files in scope.</summary>
    Uniques
}

/// <summary>
/// Hash algorithm used for duplicate/unique comparison.
/// Never mixes different algorithms; each value is only compared against itself.
/// </summary>
public enum ComparisonAlgorithm
{
    None,
    Sha256,
    Crc32
}

public enum AdvancedCatalogField
{
    Name,
    FullPath,
    Extension,
    DocumentType,
    SizeBytes,
    Status,
    CustomProperty,
    Metadata,
    ActiveLoan,
    Borrower,
    OverdueLoan
}

public enum AdvancedCatalogOperator
{
    Contains,
    Equals,
    NotEquals,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    OneOf,
    IsMissing,
    IsPresent
}

/// <summary>
/// One typed predicate. <see cref="Key"/> is required for <see cref="AdvancedCatalogField.CustomProperty"/>
/// and <see cref="AdvancedCatalogField.Metadata"/> and must be null otherwise.
/// <see cref="Value"/> is unused for <see cref="AdvancedCatalogOperator.IsMissing"/> and
/// <see cref="AdvancedCatalogOperator.IsPresent"/>; <see cref="Values"/> is only used for
/// <see cref="AdvancedCatalogOperator.OneOf"/>.
/// </summary>
public sealed record AdvancedCatalogPredicate
{
    public AdvancedCatalogField Field { get; init; }

    public AdvancedCatalogOperator Operator { get; init; }

    public string? Key { get; init; }

    public object? Value { get; init; }

    public IReadOnlyList<object?>? Values { get; init; }

    public void Validate()
    {
        var needsKey = Field is AdvancedCatalogField.CustomProperty or AdvancedCatalogField.Metadata;
        if (needsKey && string.IsNullOrWhiteSpace(Key))
        {
            throw new Exceptions.AdvancedCatalogQueryException(
                "Predicate for field '{0}' requires a non-empty key.",
                Field.ToString());
        }

        if (!needsKey && !string.IsNullOrWhiteSpace(Key))
        {
            throw new Exceptions.AdvancedCatalogQueryException(
                "Predicate for field '{0}' must not specify a key.",
                Field.ToString());
        }

        if (Operator is AdvancedCatalogOperator.IsMissing or AdvancedCatalogOperator.IsPresent)
        {
            return;
        }

        if (Operator == AdvancedCatalogOperator.OneOf)
        {
            if (Values is null || Values.Count == 0)
            {
                throw new Exceptions.AdvancedCatalogQueryException(
                    "Operator '{0}' requires at least one comparison value.",
                    Operator.ToString());
            }

            return;
        }

        if (Value is null)
        {
            throw new Exceptions.AdvancedCatalogQueryException(
                "Operator '{0}' requires a comparison value.",
                Operator.ToString());
        }
    }
}
