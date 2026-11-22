using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search.Exceptions;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.Presentation.Search;

/// <summary>
/// Repository-backed typed evaluation of <see cref="AdvancedCatalogQuery"/>.
/// </summary>
/// <remarks>
/// Semantics (version 1):
/// <list type="bullet">
/// <item>Text comparisons use ordinal case-insensitive matching on stored values, never on localized display strings.</item>
/// <item>Missing values only match <see cref="AdvancedCatalogOperator.IsMissing"/>; every other operator returns false for them.</item>
/// <item>Date comparisons compare instants (<see cref="DateTimeOffset.UtcDateTime"/>); offsets and time zones do not affect equality.</item>
/// <item>Numeric comparisons use raw stored numbers with invariant parsing; byte units (B/KB/MB/GB/TB) convert, other units require an exact unit match.</item>
/// <item>Unknown plugin/metadata keys evaluate safely: they match <c>IsMissing</c> and never throw.</item>
/// </list>
/// Results are deterministically ordered by full path (ordinal ignore-case) then id (ordinal),
/// support <see cref="AdvancedCatalogQuery.Skip"/>/<see cref="AdvancedCatalogQuery.Take"/> pagination,
/// and honor <see cref="CancellationToken"/> throughout evaluation.
/// </remarks>
public static class AdvancedCatalogSearchService
{
    public const int MaxTake = 1000;

    /// <summary>Raw typed metadata per entry (stored payloads, not display strings). Unknown keys are simply absent.</summary>
    public delegate IReadOnlyDictionary<string, object?> MetadataLookup(CatalogDocument document);

    public static IReadOnlyList<CatalogFindResult> Search(
        CatalogSearchIndex index,
        AdvancedCatalogQuery query,
        MetadataLookup? metadataLookup = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ContributedSearchFieldDefinition>? contributedFields = null) =>
        Search(index, query, metadataLookup, cancellationToken, out _, contributedFields);

    internal static IReadOnlyList<CatalogFindResult> Search(
        CatalogSearchIndex index,
        AdvancedCatalogQuery query,
        MetadataLookup? metadataLookup,
        CancellationToken cancellationToken,
        out int totalMatches,
        IReadOnlyList<ContributedSearchFieldDefinition>? contributedFields = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        IReadOnlyDictionary<string, ContributedSearchFieldDefinition>? contributed =
            contributedFields is { Count: > 0 }
                ? contributedFields
                    .GroupBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
                : null;

        var candidates = index.GetScopeCandidates(query.Scope);

        // Apply duplicate/unique classification as a pre-filter before predicates.
        if (query.DuplicateFilter != DuplicateFilterMode.None)
        {
            candidates = ClassifyByDuplicateStatus(index, candidates, query, metadataLookup, cancellationToken);
        }

        var matches = new List<(CatalogFindResult Result, string SortPath)>(candidates.Count);
        var checkedCount = 0;

        foreach (var document in candidates)
        {
            if ((++checkedCount & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            IReadOnlyDictionary<string, object?> metadata =
                metadataLookup?.Invoke(document) ?? (IReadOnlyDictionary<string, object?>)document.Properties;
            var fullPath = index.GetFullPath(document.Id);
            if (MatchesGroup(query.Root, document, fullPath, metadata, contributed, cancellationToken))
            {
                var context = ContextFromPath(fullPath);
                matches.Add((new CatalogFindResult(document, fullPath, context), fullPath));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        totalMatches = matches.Count;
        var ordered = AdvancedCatalogFindDialogViewModel.SortMatches(
            matches.Select(match => match.Result).ToArray(), query.SortKey, query.SortDescending);

        var take = Math.Clamp(query.Take <= 0 ? 200 : query.Take, 1, MaxTake);
        var skip = Math.Max(0, query.Skip);
        if (skip >= ordered.Count)
        {
            return [];
        }

        return ordered.Skip(skip).Take(take).ToArray();
    }

    public static void Validate(AdvancedCatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Version != AdvancedCatalogQuery.CurrentVersion)
        {
            throw new AdvancedCatalogQueryException(
                "Unsupported query version '{0}'. Expected version '{1}'.",
                query.Version,
                AdvancedCatalogQuery.CurrentVersion);
        }

        if (query.Scope is null || !Enum.IsDefined(query.SortKey)
            || query.Scope.Types?.Any(type => !Enum.IsDefined(type)) == true)
        {
            throw new AdvancedCatalogQueryException("Query scope or sort settings are invalid.");
        }

        if (query.Skip < 0)
        {
            throw new AdvancedCatalogQueryException("Skip must be zero or positive, but was '{0}'.", query.Skip);
        }

        if (query.Take is < 1 or > MaxTake)
        {
            throw new AdvancedCatalogQueryException(
                "Take must be between 1 and '{0}', but was '{1}'.",
                MaxTake,
                query.Take);
        }

        if (!Enum.IsDefined(query.DuplicateFilter))
        {
            throw new AdvancedCatalogQueryException("Duplicate filter mode is invalid.");
        }

        if (!Enum.IsDefined(query.HashAlgorithm))
        {
            throw new AdvancedCatalogQueryException("Hash algorithm is invalid.");
        }

        if (query.DuplicateFilter != DuplicateFilterMode.None && query.HashAlgorithm == ComparisonAlgorithm.None)
        {
            throw new AdvancedCatalogQueryException(
                "A hash algorithm is required when duplicate filtering is enabled.");
        }

        ValidateGroup(query.Root);
    }

    private static void ValidateGroup(CatalogQueryGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!Enum.IsDefined(group.Combination) || group.Predicates is null || group.Groups is null)
        {
            throw new AdvancedCatalogQueryException("Query group is invalid.");
        }
        foreach (var predicate in group.Predicates)
        {
            if (predicate is null || !Enum.IsDefined(predicate.Field) || !Enum.IsDefined(predicate.Operator))
            {
                throw new AdvancedCatalogQueryException("Query predicate is invalid.");
            }
            predicate.Validate();
        }

        foreach (var nested in group.Groups)
        {
            ValidateGroup(nested);
        }
    }

    private static bool MatchesGroup(
        CatalogQueryGroup group,
        CatalogDocument document,
        string fullPath,
        IReadOnlyDictionary<string, object?> metadata,
        IReadOnlyDictionary<string, ContributedSearchFieldDefinition>? contributed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasClauses = group.Predicates.Count > 0 || group.Groups.Count > 0;
        if (!hasClauses)
        {
            return true;
        }

        if (group.Combination == CatalogQueryCombination.All)
        {
            foreach (var predicate in group.Predicates)
            {
                if (!MatchesPredicate(predicate, document, fullPath, metadata, contributed))
                {
                    return false;
                }
            }

            foreach (var nested in group.Groups)
            {
                if (!MatchesGroup(nested, document, fullPath, metadata, contributed, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var predicate in group.Predicates)
        {
            if (MatchesPredicate(predicate, document, fullPath, metadata, contributed))
            {
                return true;
            }
        }

        foreach (var nested in group.Groups)
        {
            if (MatchesGroup(nested, document, fullPath, metadata, contributed, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPredicate(
        AdvancedCatalogPredicate predicate,
        CatalogDocument document,
        string fullPath,
        IReadOnlyDictionary<string, object?> metadata,
        IReadOnlyDictionary<string, ContributedSearchFieldDefinition>? contributed)
    {
        return predicate.Field switch
        {
            AdvancedCatalogField.Name => MatchText(StoredText(document.Name), predicate),
            AdvancedCatalogField.FullPath => MatchText(fullPath, predicate),
            AdvancedCatalogField.Extension => MatchText(CatalogSearchIndex.NormalizeExtension(document.Name), predicate),
            AdvancedCatalogField.DocumentType => MatchType(document.Type, predicate),
            AdvancedCatalogField.SizeBytes => MatchNumber(Convert.ToDouble(document.Size, CultureInfo.InvariantCulture), document.HasKnownSize, null, predicate),
            AdvancedCatalogField.Status => MatchText(StoredText(document.Status), predicate),
            AdvancedCatalogField.CustomProperty => MatchCustomProperty(document, predicate),
            AdvancedCatalogField.Metadata => MatchMetadata(metadata, predicate),
            AdvancedCatalogField.Location => MatchText(StoredText(document.Location), predicate),
            AdvancedCatalogField.Contributed => MatchContributed(document, predicate, contributed),
            _ => false
        };
    }

    private static bool MatchContributed(
        CatalogDocument document,
        AdvancedCatalogPredicate predicate,
        IReadOnlyDictionary<string, ContributedSearchFieldDefinition>? contributed)
    {
        if (contributed is null || predicate.Key is not { Length: > 0 } key ||
            !contributed.TryGetValue(key, out var field))
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
        }

        if (field.SelectValue is null)
        {
            return predicate.Operator switch
            {
                AdvancedCatalogOperator.IsPresent => field.Matches(document),
                AdvancedCatalogOperator.IsMissing => !field.Matches(document),
                _ => false
            };
        }

        return MatchText(StoredText(field.SelectValue(document)), predicate);
    }

    private static bool MatchCustomProperty(CatalogDocument document, AdvancedCatalogPredicate predicate)
    {
        // A missing key and an empty (or all-missing) value are equivalent here:
        // persistence layers may drop empty collections, so both count as missing
        // and both satisfy NotEquals.
        if (!document.Properties.TryGetValue(predicate.Key!, out var raw) || IsMissingValue(raw))
        {
            return predicate.Operator is AdvancedCatalogOperator.IsMissing or AdvancedCatalogOperator.NotEquals;
        }

        return MatchStoredValue(raw, unit: null, predicate);
    }

    private static bool MatchMetadata(IReadOnlyDictionary<string, object?> metadata, AdvancedCatalogPredicate predicate)
    {
        object? raw = null;
        var found = false;
        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, predicate.Key, StringComparison.OrdinalIgnoreCase))
            {
                raw = pair.Value;
                found = true;
                break;
            }
        }

        if (!found)
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
        }

        return MatchStoredValue(raw, unit: ExtractUnit(raw), predicate);
    }

    private static bool MatchStoredValue(object? raw, string? unit, AdvancedCatalogPredicate predicate)
    {
        if (raw is System.Collections.IEnumerable sequence && raw is not string)
        {
            return MatchSequence(sequence, unit, predicate);
        }

        if (IsMissingValue(raw))
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsMissing)
        {
            return false;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsPresent)
        {
            return true;
        }

        if (TryGetInstant(raw, out var instant))
        {
            return MatchInstant(instant, unit, predicate);
        }

        if (TryGetNumber(raw, out var number))
        {
            return MatchNumber(number, hasValue: true, unit, predicate);
        }

        return MatchText(Convert.ToString(raw, CultureInfo.InvariantCulture), predicate);
    }

    /// <summary>
    /// Matches multi-value properties (such as the tag id list): an empty or
    /// all-missing sequence counts as missing, and also as not equal to any
    /// value. Otherwise any present item may satisfy the predicate, while
    /// NotEquals holds when no present item is equal.
    /// </summary>
    private static bool MatchSequence(System.Collections.IEnumerable sequence, string? unit, AdvancedCatalogPredicate predicate)
    {
        var items = new List<object?>();
        foreach (var item in sequence)
        {
            items.Add(item);
        }

        if (items.All(IsMissingValue))
        {
            return predicate.Operator is AdvancedCatalogOperator.IsMissing or AdvancedCatalogOperator.NotEquals;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsMissing)
        {
            return false;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsPresent)
        {
            return true;
        }

        if (predicate.Operator == AdvancedCatalogOperator.NotEquals)
        {
            return !items.Any(item => !IsMissingValue(item) &&
                MatchStoredValue(item, ExtractUnit(item) ?? unit, predicate with { Operator = AdvancedCatalogOperator.Equals }));
        }

        return items.Any(item => !IsMissingValue(item) &&
            MatchStoredValue(item, ExtractUnit(item) ?? unit, predicate));
    }

    private static bool MatchType(CatalogDocumentType type, AdvancedCatalogPredicate predicate)
    {
        var stored = type.ToString();
        return predicate.Operator switch
        {
            AdvancedCatalogOperator.IsMissing => false,
            AdvancedCatalogOperator.IsPresent => true,
            _ => MatchText(stored, predicate)
        };
    }

    private static bool MatchText(string? stored, AdvancedCatalogPredicate predicate)
    {
        if (IsMissingValue(stored))
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
        }

        var value = stored ?? string.Empty;
        switch (predicate.Operator)
        {
            case AdvancedCatalogOperator.IsMissing:
                return false;
            case AdvancedCatalogOperator.IsPresent:
                return true;
            case AdvancedCatalogOperator.Contains:
                return predicate.Value is string text && value.Contains(text, StringComparison.OrdinalIgnoreCase);
            case AdvancedCatalogOperator.Equals:
                return predicate.Value is string equals &&
                    string.Equals(value, equals, StringComparison.OrdinalIgnoreCase);
            case AdvancedCatalogOperator.NotEquals:
                return predicate.Value is string notEquals &&
                    !string.Equals(value, notEquals, StringComparison.OrdinalIgnoreCase);
            case AdvancedCatalogOperator.StartsWith:
                return predicate.Value is string prefix && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            case AdvancedCatalogOperator.EndsWith:
                return predicate.Value is string suffix && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            case AdvancedCatalogOperator.OneOf:
                return predicate.Values is not null &&
                    predicate.Values.OfType<string>().Any(option =>
                        string.Equals(value, option, StringComparison.OrdinalIgnoreCase));
            case AdvancedCatalogOperator.GreaterThan:
            case AdvancedCatalogOperator.GreaterThanOrEqual:
            case AdvancedCatalogOperator.LessThan:
            case AdvancedCatalogOperator.LessThanOrEqual:
                if (TryGetNumber(value, out var left) && TryGetNumber(predicate.Value, out var right))
                {
                    return CompareNumbers(left, right, predicate.Operator);
                }

                return false;
            default:
                return false;
        }
    }

    private static bool MatchNumber(double stored, bool hasValue, string? unit, AdvancedCatalogPredicate predicate)
    {
        if (!hasValue)
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsMissing)
        {
            return false;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsPresent)
        {
            return true;
        }

        if (predicate.Operator == AdvancedCatalogOperator.OneOf)
        {
            return predicate.Values is not null &&
                predicate.Values.Any(option => TryGetNumber(option, out var candidate) &&
                    ConvertUnits(candidate, ExtractUnit(option), unit) is double converted &&
                    stored.Equals(converted));
        }

        if (!TryGetNumber(predicate.Value, out var comparison))
        {
            return false;
        }

        var normalized = ConvertUnits(comparison, ExtractUnit(predicate.Value), unit);
        if (normalized is null)
        {
            return false;
        }

        return predicate.Operator switch
        {
            AdvancedCatalogOperator.Equals => stored.Equals(normalized.Value),
            AdvancedCatalogOperator.NotEquals => !stored.Equals(normalized.Value),
            AdvancedCatalogOperator.GreaterThan => stored > normalized.Value,
            AdvancedCatalogOperator.GreaterThanOrEqual => stored >= normalized.Value,
            AdvancedCatalogOperator.LessThan => stored < normalized.Value,
            AdvancedCatalogOperator.LessThanOrEqual => stored <= normalized.Value,
            AdvancedCatalogOperator.Contains => Convert.ToString(stored, CultureInfo.InvariantCulture)
                .Contains(Convert.ToString(predicate.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            AdvancedCatalogOperator.StartsWith => Convert.ToString(stored, CultureInfo.InvariantCulture)
                .StartsWith(Convert.ToString(predicate.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            AdvancedCatalogOperator.EndsWith => Convert.ToString(stored, CultureInfo.InvariantCulture)
                .EndsWith(Convert.ToString(predicate.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool MatchInstant(DateTimeOffset instant, string? unit, AdvancedCatalogPredicate predicate)
    {
        if (!string.IsNullOrWhiteSpace(unit))
        {
            return false;
        }

        if (predicate.Operator == AdvancedCatalogOperator.IsPresent)
        {
            return true;
        }

        if (predicate.Operator == AdvancedCatalogOperator.OneOf)
        {
            return predicate.Values is not null &&
                predicate.Values.Any(option => TryGetInstant(option, out var candidate) &&
                    instant.UtcDateTime.Equals(candidate.UtcDateTime));
        }

        if (!TryGetInstant(predicate.Value, out var comparison))
        {
            return predicate.Operator == AdvancedCatalogOperator.NotEquals && predicate.Value is string;
        }

        var left = instant.UtcDateTime;
        var right = comparison.UtcDateTime;
        return predicate.Operator switch
        {
            AdvancedCatalogOperator.Equals => left.Equals(right),
            AdvancedCatalogOperator.NotEquals => !left.Equals(right),
            AdvancedCatalogOperator.GreaterThan => left > right,
            AdvancedCatalogOperator.GreaterThanOrEqual => left >= right,
            AdvancedCatalogOperator.LessThan => left < right,
            AdvancedCatalogOperator.LessThanOrEqual => left <= right,
            _ => false
        };
    }

    private static bool CompareNumbers(double left, double right, AdvancedCatalogOperator op) => op switch
    {
        AdvancedCatalogOperator.GreaterThan => left > right,
        AdvancedCatalogOperator.GreaterThanOrEqual => left >= right,
        AdvancedCatalogOperator.LessThan => left < right,
        AdvancedCatalogOperator.LessThanOrEqual => left <= right,
        AdvancedCatalogOperator.Equals => left.Equals(right),
        AdvancedCatalogOperator.NotEquals => !left.Equals(right),
        _ => false
    };

    private static bool IsMissingValue(object? raw) => raw is null ||
        (raw is string text && string.IsNullOrWhiteSpace(text));

    private static string? StoredText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryGetNumber(object? raw, out double number)
    {
        switch (raw)
        {
            case null:
                number = 0;
                return false;
            case double d when double.IsFinite(d):
                number = d;
                return true;
            case float f when float.IsFinite(f):
                number = f;
                return true;
            case long l:
                number = l;
                return true;
            case int i:
                number = i;
                return true;
            case bool:
                number = 0;
                return false;
            case DateTime:
            case DateTimeOffset:
                number = 0;
                return false;
            default:
                return double.TryParse(
                    Convert.ToString(raw, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number);
        }
    }

    private static bool TryGetInstant(object? raw, out DateTimeOffset instant)
    {
        switch (raw)
        {
            case DateTimeOffset offset:
                instant = offset;
                return true;
            case DateTime dateTime:
                instant = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                return true;
            case string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed):
                instant = parsed;
                return true;
            default:
                instant = default;
                return false;
        }
    }

    private static string? ExtractUnit(object? raw)
    {
        var text = raw as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : null;
    }

    private static double? ConvertUnits(double value, string? fromUnit, string? toUnit)
    {
        if (string.Equals(fromUnit, toUnit, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (TryByteScale(fromUnit) is double from && TryByteScale(toUnit) is double to)
        {
            return value * from / to;
        }

        return null;
    }

    private static double? TryByteScale(string? unit) => unit?.Trim().ToUpperInvariant() switch
    {
        null or "" or "B" => 1,
        "KB" => 1024,
        "MB" => 1024 * 1024,
        "GB" => 1024 * 1024 * 1024,
        "TB" => 1024L * 1024 * 1024 * 1024,
        _ => null
    };

    private static string ContextFromPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return string.Empty;
        }

        var separator = fullPath.LastIndexOf(" / ", StringComparison.Ordinal);
        return separator < 0 ? fullPath : fullPath.Substring(0, separator);
    }

    /// <summary>
    /// Classifies file entries by (size, normalized hash) across the full scope, then
    /// filters to duplicates (groups of 2+) or uniques (singletons). Classification is
    /// performed on all file entries before any predicate filtering, so a name filter
    /// cannot hide the other member of a duplicate group.
    /// </summary>
    private static IReadOnlyList<CatalogDocument> ClassifyByDuplicateStatus(
        CatalogSearchIndex index,
        IReadOnlyList<CatalogDocument> candidates,
        AdvancedCatalogQuery query,
        MetadataLookup? metadataLookup,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<(long Size, string Hash), List<CatalogDocument>>(
            EqualityComparer<(long, string)>.Default);

        foreach (var document in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (document.Type != CatalogDocumentType.File)
            {
                continue;
            }

            var hash = TryNormalizeHash(document, query.HashAlgorithm, metadataLookup);
            if (hash is null)
            {
                continue;
            }

            var key = (document.Size, hash);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(document);
        }

        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.Values)
        {
            if (query.DuplicateFilter == DuplicateFilterMode.Duplicates && group.Count >= 2)
            {
                foreach (var doc in group)
                {
                    resultIds.Add(doc.Id);
                }
            }
            else if (query.DuplicateFilter == DuplicateFilterMode.Uniques && group.Count == 1)
            {
                resultIds.Add(group[0].Id);
            }
        }

        return candidates.Where(doc => resultIds.Contains(doc.Id)).ToArray();
    }

    /// <summary>
    /// Attempts to extract and normalize a hash value for the given document using the
    /// specified algorithm. Returns null if the hash is missing, invalid, or ambiguous.
    /// </summary>
    private static string? TryNormalizeHash(
        CatalogDocument document,
        ComparisonAlgorithm algorithm,
        MetadataLookup? metadataLookup)
    {
        return algorithm switch
        {
            ComparisonAlgorithm.Sha256 => TryNormalizeSha256(document, metadataLookup),
            ComparisonAlgorithm.Crc32 => TryNormalizeCrc32(document, metadataLookup),
            _ => null
        };
    }

    private static string? TryNormalizeSha256(
        CatalogDocument document,
        MetadataLookup? metadataLookup)
    {
        var raw = LookupHashValue(document, "sha256", metadataLookup);
        if (raw is not string text)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Length != 64)
        {
            return null;
        }

        foreach (var c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                return null;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static string? TryNormalizeCrc32(
        CatalogDocument document,
        MetadataLookup? metadataLookup)
    {
        var raw = LookupHashValue(document, "crc32", metadataLookup);
        if (raw is null)
        {
            return null;
        }

        if (raw is uint u)
        {
            return u.ToString("x8", CultureInfo.InvariantCulture);
        }

        if (raw is int or long)
        {
            var num = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return ((uint)num).ToString("x8", CultureInfo.InvariantCulture);
        }

        if (raw is not string text)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // Handle "0x" prefixed hex.
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2)
        {
            var hexPart = trimmed[2..];
            if (hexPart.Length <= 8
                && uint.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVal))
            {
                return hexVal.ToString("x8", CultureInfo.InvariantCulture);
            }

            return null;
        }

        // Exactly 8 hex chars: hex if contains a-f, ambiguous if all digits.
        if (trimmed.Length == 8 && IsAllHexDigits(trimmed))
        {
            if (ContainsAlphaHexDigit(trimmed))
            {
                return trimmed.ToLowerInvariant();
            }

            // All digits 0-9, exactly 8 chars → ambiguous → exclude.
            return null;
        }

        // All digits, not 8 chars → decimal.
        if (IsAllDigits(trimmed) && trimmed.Length != 8
            && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalVal))
        {
            return ((uint)decimalVal).ToString("x8", CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>
    /// Looks up a hash value from metadata (plugin-provided) or catalog properties.
    /// Uses case-insensitive key matching for property lookups.
    /// </summary>
    private static object? LookupHashValue(
        CatalogDocument document,
        string key,
        MetadataLookup? metadataLookup)
    {
        if (metadataLookup is not null)
        {
            var metadata = metadataLookup(document);
            if (metadata is not null && TryGetValueIgnoreCase(metadata, key, out var metaVal) && metaVal is not null)
            {
                return metaVal;
            }
        }

        if (TryGetValueIgnoreCase(document.Properties, key, out var propVal))
        {
            return propVal;
        }

        return null;
    }

    private static bool TryGetValueIgnoreCase(
        IReadOnlyDictionary<string, object?> dict,
        string key,
        out object? value)
    {
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool IsAllHexDigits(string text)
    {
        foreach (var c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAlphaHexDigit(string text)
    {
        foreach (var c in text)
        {
            if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllDigits(string text)
    {
        foreach (var c in text)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
