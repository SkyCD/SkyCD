using System;
using System.Globalization;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

/// <summary>The value kind carried by a <see cref="TemplateValue"/>.</summary>
internal enum TemplateValueKind
{
    Text,
    Number,
    Bool
}

/// <summary>
/// A dynamic template value: text, a number, or a boolean. Variables resolve to text, comparisons and
/// predicates yield booleans, and arithmetic yields numbers, so a single value type flows through the AST.
/// </summary>
internal readonly struct TemplateValue
{
    private readonly string? text;
    private readonly double number;

    private TemplateValue(TemplateValueKind kind, string? text, double number)
    {
        Kind = kind;
        this.text = text;
        this.number = number;
    }

    public TemplateValueKind Kind { get; }

    public static TemplateValue Text(string value) => new(TemplateValueKind.Text, value ?? string.Empty, 0);

    public static TemplateValue Number(double value) => new(TemplateValueKind.Number, null, value);

    public static TemplateValue Bool(bool value) => new(TemplateValueKind.Bool, null, value ? 1 : 0);

    /// <summary>The user-visible rendering: text verbatim, numbers compact, booleans as true/false.</summary>
    public string Display() => Kind switch
    {
        TemplateValueKind.Text => text ?? string.Empty,
        TemplateValueKind.Number => FormatNumber(number),
        TemplateValueKind.Bool => number != 0 ? "true" : "false",
        _ => string.Empty
    };

    /// <summary>Truthiness: booleans directly, numbers as non-zero, text as a non-empty value that is not "0".</summary>
    public bool IsTruthy() => Kind switch
    {
        TemplateValueKind.Bool => number != 0,
        TemplateValueKind.Number => number != 0,
        _ => !string.IsNullOrWhiteSpace(text) && !string.Equals(text!.Trim(), "0", StringComparison.OrdinalIgnoreCase)
    };

    public double AsNumber() => Kind switch
    {
        TemplateValueKind.Number => number,
        TemplateValueKind.Bool => number,
        _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN
    };

    public string AsText() => Kind switch
    {
        TemplateValueKind.Text => text ?? string.Empty,
        _ => Display()
    };

    /// <summary>True when either side is a genuine text value, forcing comparison and concatenation to be textual.</summary>
    public bool IsTextual => Kind == TemplateValueKind.Text;

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return string.Empty;
        return value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
