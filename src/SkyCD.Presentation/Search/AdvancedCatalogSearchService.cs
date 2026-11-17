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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        var candidates = index.GetScopeCandidates(query.Scope);
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
            if (MatchesGroup(query.Root, document, fullPath, metadata, cancellationToken))
            {
                var context = ContextFromPath(fullPath);
                matches.Add((new CatalogFindResult(document, fullPath, context), fullPath));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
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
                if (!MatchesPredicate(predicate, document, fullPath, metadata))
                {
                    return false;
                }
            }

            foreach (var nested in group.Groups)
            {
                if (!MatchesGroup(nested, document, fullPath, metadata, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var predicate in group.Predicates)
        {
            if (MatchesPredicate(predicate, document, fullPath, metadata))
            {
                return true;
            }
        }

        foreach (var nested in group.Groups)
        {
            if (MatchesGroup(nested, document, fullPath, metadata, cancellationToken))
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
        IReadOnlyDictionary<string, object?> metadata)
    {
        return predicate.Field switch
        {
            AdvancedCatalogField.Name => MatchText(StoredText(document.Name), predicate),
            AdvancedCatalogField.FullPath => MatchText(fullPath, predicate),
            AdvancedCatalogField.Extension => MatchText(CatalogSearchIndex.NormalizeExtension(document.Name), predicate),
            AdvancedCatalogField.DocumentType => MatchType(document.Type, predicate),
            AdvancedCatalogField.SizeBytes => MatchNumber(Convert.ToDouble(document.Size, CultureInfo.InvariantCulture), true, null, predicate),
            AdvancedCatalogField.Status => MatchText(StoredText(document.Status), predicate),
            AdvancedCatalogField.CustomProperty => MatchCustomProperty(document, predicate),
            AdvancedCatalogField.Metadata => MatchMetadata(metadata, predicate),
            _ => false
        };
    }

    private static bool MatchCustomProperty(CatalogDocument document, AdvancedCatalogPredicate predicate)
    {
        if (!document.Properties.TryGetValue(predicate.Key!, out var raw))
        {
            return predicate.Operator == AdvancedCatalogOperator.IsMissing;
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
}
