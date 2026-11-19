using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Presentation.Search;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One editable advanced-search predicate. Values stay in text form; the search
/// service parses numbers and dates with invariant culture, so the editor never
/// interprets localized display strings.
/// </summary>
public sealed partial class AdvancedCatalogFilterRowViewModel : ObservableObject
{
    public IReadOnlyList<AdvancedCatalogField> Fields { get; } =
        Enum.GetValues<AdvancedCatalogField>();

    private static readonly AdvancedCatalogOperator[] AllOperators = Enum.GetValues<AdvancedCatalogOperator>();
    private static readonly AdvancedCatalogOperator[] PresenceOperators =
        [AdvancedCatalogOperator.IsPresent, AdvancedCatalogOperator.IsMissing];
    private static readonly AdvancedCatalogOperator[] TextOperators = AllOperators
        .Where(static op => op is not (AdvancedCatalogOperator.GreaterThan or AdvancedCatalogOperator.GreaterThanOrEqual
            or AdvancedCatalogOperator.LessThan or AdvancedCatalogOperator.LessThanOrEqual))
        .ToArray();

    public IReadOnlyList<AdvancedCatalogOperator> Operators
    {
        get
        {
            var compatible = SelectedField switch
            {
                AdvancedCatalogField.ActiveLoan or AdvancedCatalogField.OverdueLoan => PresenceOperators,
                AdvancedCatalogField.Name or AdvancedCatalogField.FullPath or AdvancedCatalogField.Extension
                    or AdvancedCatalogField.DocumentType or AdvancedCatalogField.Status
                    or AdvancedCatalogField.Borrower => TextOperators,
                _ => AllOperators
            };

            // A previously saved rule remains editable even if its operator is no longer suggested.
            return compatible.Contains(SelectedOperator) ? compatible : [.. compatible, SelectedOperator];
        }
    }

    [ObservableProperty]
    private AdvancedCatalogField selectedField = AdvancedCatalogField.Name;

    [ObservableProperty]
    private AdvancedCatalogOperator selectedOperator = AdvancedCatalogOperator.Contains;

    [ObservableProperty]
    private string key = string.Empty;

    [ObservableProperty]
    private string valueText = string.Empty;

    public bool IsKeyed => SelectedField is AdvancedCatalogField.CustomProperty or AdvancedCatalogField.Metadata;

    public bool IsValueRequired => SelectedOperator is not
        AdvancedCatalogOperator.IsMissing and not AdvancedCatalogOperator.IsPresent;

    partial void OnSelectedFieldChanged(AdvancedCatalogField value)
    {
        OnPropertyChanged(nameof(IsKeyed));
        OnPropertyChanged(nameof(Operators));
        if (value is AdvancedCatalogField.ActiveLoan or AdvancedCatalogField.OverdueLoan)
        {
            SelectedOperator = AdvancedCatalogOperator.IsPresent;
        }
        else if (value is AdvancedCatalogField.Borrower)
        {
            SelectedOperator = AdvancedCatalogOperator.Contains;
        }
        else if (value is AdvancedCatalogField.SizeBytes)
        {
            SelectedOperator = AdvancedCatalogOperator.Equals;
        }
        else if (SelectedOperator is AdvancedCatalogOperator.GreaterThan
            or AdvancedCatalogOperator.GreaterThanOrEqual
            or AdvancedCatalogOperator.LessThan
            or AdvancedCatalogOperator.LessThanOrEqual)
        {
            SelectedOperator = AdvancedCatalogOperator.Contains;
        }
    }

    partial void OnSelectedOperatorChanged(AdvancedCatalogOperator value)
    {
        OnPropertyChanged(nameof(IsValueRequired));
        OnPropertyChanged(nameof(Operators));
    }

    /// <summary>
    /// Builds the typed predicate, or returns false with a human-readable reason
    /// when the row is incomplete. <c>OneOf</c> splits <see cref="ValueText"/> on
    /// semicolons so one row can list several accepted values.
    /// </summary>
    public bool TryBuildPredicate(out AdvancedCatalogPredicate? predicate, out string? error)
    {
        predicate = null;
        error = null;
        var key = Key?.Trim() ?? string.Empty;
        if (IsKeyed && string.IsNullOrWhiteSpace(key))
        {
            error = LocalizationService.Current.Get(TextKeys.AdvancedFilterKeyRequired);
            return false;
        }

        var value = ValueText?.Trim() ?? string.Empty;
        if (IsValueRequired && string.IsNullOrEmpty(value) && SelectedOperator != AdvancedCatalogOperator.OneOf)
        {
            error = LocalizationService.Current.Get(TextKeys.AdvancedFilterValueRequired);
            return false;
        }

        if (SelectedOperator == AdvancedCatalogOperator.OneOf)
        {
            var values = value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static option => option.Length > 0)
                .Select(static option => (object?)option)
                .ToArray();
            if (values.Length == 0)
            {
                error = LocalizationService.Current.Get(TextKeys.AdvancedFilterOneOfRequired);
                return false;
            }

            predicate = new AdvancedCatalogPredicate
            {
                Field = SelectedField,
                Operator = SelectedOperator,
                Key = IsKeyed ? key : null,
                Values = values
            };
            return true;
        }

        predicate = new AdvancedCatalogPredicate
        {
            Field = SelectedField,
            Operator = SelectedOperator,
            Key = IsKeyed ? key : null,
            Value = IsValueRequired ? value : null
        };
        return true;
    }
}
