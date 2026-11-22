using System;
using System.Collections.Generic;
using System.Globalization;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Models;

/// <summary>
/// One row read from a Jet table, paired with the table's column order. Values are
/// accessed by name so older catalogs that predate a column simply return null.
/// </summary>
internal sealed class SmartCdRow
{
    private readonly Dictionary<string, int> ordinals;
    private readonly object?[] values;

    public SmartCdRow(string tableName, IEnumerable<string> columns, object?[] values)
    {
        TableName = tableName;
        this.values = values;
        ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 0;
        foreach (var column in columns)
        {
            ordinals[column] = ordinal++;
        }
    }

    public string TableName { get; }

    public bool HasColumn(string column) => ordinals.ContainsKey(column);

    public object? Get(string column)
    {
        if (!ordinals.TryGetValue(column, out var index) || index >= values.Length)
        {
            return null;
        }

        var value = values[index];
        return value is DBNull ? null : value;
    }

    public string? GetString(string column) => Get(column) switch
    {
        null => null,
        string text => text,
        var value => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    public int? GetInt32(string column) => ConvertToInt64(Get(column)) is { } value &&
                                           value is >= int.MinValue and <= int.MaxValue
        ? (int)value
        : null;

    public short? GetInt16(string column) => ConvertToInt64(Get(column)) is { } value &&
                                             value is >= short.MinValue and <= short.MaxValue
        ? (short)value
        : null;

    public long? GetInt64(string column) => ConvertToInt64(Get(column));

    public double? GetDouble(string column) => Get(column) switch
    {
        null => null,
        double value => value,
        float value => value,
        decimal value => (double)value,
        byte or short or int or long => Convert.ToDouble(Get(column), CultureInfo.InvariantCulture),
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    public bool? GetBoolean(string column) => Get(column) switch
    {
        null => null,
        bool value => value,
        byte value => value != 0,
        short value => value != 0,
        int value => value != 0,
        long value => value != 0,
        string text when bool.TryParse(text, out var parsed) => parsed,
        "-1" => true,
        "0" => false,
        _ => null
    };

    public DateTime? GetDateTime(string column) => Get(column) switch
    {
        null => null,
        DateTime value => value,
        string text when DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed) => parsed,
        _ => null
    };

    private static long? ConvertToInt64(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return value switch
            {
                byte or short or int or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
                decimal number when number == decimal.Truncate(number) && number is >= long.MinValue and <= long.MaxValue => (long)number,
                double number when double.IsFinite(number) && number == Math.Truncate(number) && number is >= long.MinValue and <= long.MaxValue => (long)number,
                float number when float.IsFinite(number) && number == MathF.Truncate(number) && number is >= long.MinValue and <= long.MaxValue => (long)number,
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
