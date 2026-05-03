using Couchbase.Lite;
using System.Collections;
using System.Globalization;
using System.Reflection;
using SkyCD.Couchbase.Decorators;

namespace SkyCD.Couchbase.Mapping;

public static class DocumentMappingExtensions
{

    public static MutableDocument ToMutableDocument<T>(this T source, string id)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var document = new MutableDocument(id);
        new MutableDictionaryDecorator(document).WriteObject(source);
        return document;
    }

    public static T? FromDocument<T>(this Document? document)
        where T : class, new()
    {
        if (document is null)
        {
            return null;
        }

        return (T?)ReadObjectFromDictionary(document, typeof(T));
    }

    private static object? ReadObjectFromDictionary(IDictionaryObject dictionary, Type targetType)
    {
        var instance = Activator.CreateInstance(targetType);
        if (instance is null)
        {
            return null;
        }

        foreach (var property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var raw = dictionary.GetValue(property.Name);
            if (raw is null)
            {
                continue;
            }

            var converted = ConvertToTargetType(raw, property.PropertyType);
            if (converted is not null)
            {
                property.SetValue(instance, converted);
            }
        }

        return instance;
    }

    private static object? ConvertToTargetType(object raw, Type targetType)
    {
        var nullableUnderlying = Nullable.GetUnderlyingType(targetType);
        var effectiveTarget = nullableUnderlying ?? targetType;

        if (effectiveTarget.IsInstanceOfType(raw))
        {
            return raw;
        }

        if (effectiveTarget.IsEnum)
        {
            if (raw is string enumString &&
                Enum.TryParse(effectiveTarget, enumString, ignoreCase: true, out var parsedEnum))
            {
                return parsedEnum;
            }

            var numericValue = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return Enum.ToObject(effectiveTarget, numericValue);
        }

        if (effectiveTarget == typeof(string))
        {
            return Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        if (effectiveTarget == typeof(DateTimeOffset))
        {
            return raw switch
            {
                DateTimeOffset dateTimeOffset => dateTimeOffset,
                DateTime dateTime => new DateTimeOffset(dateTime),
                string stringValue when DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedOffset) => parsedOffset,
                string stringValue when DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime) => new DateTimeOffset(parsedDateTime),
                long unixMilliseconds => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds),
                double unixMillisecondsDouble => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(unixMillisecondsDouble, CultureInfo.InvariantCulture)),
                _ => null
            };
        }

        if (effectiveTarget == typeof(DateTime))
        {
            return raw switch
            {
                DateTime dateTime => dateTime,
                DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
                string stringValue when DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime) => parsedDateTime,
                string stringValue when DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedOffset) => parsedOffset.UtcDateTime,
                _ => null
            };
        }

        if (effectiveTarget.IsPrimitive || effectiveTarget == typeof(decimal))
        {
            return Convert.ChangeType(raw, effectiveTarget, CultureInfo.InvariantCulture);
        }

        return raw switch
        {
            IDictionaryObject rawDictionary => ReadObjectFromDictionary(rawDictionary, effectiveTarget),
            ArrayObject rawArray => ConvertArray(rawArray, effectiveTarget),
            _ => raw
        };
    }

    private static object ConvertArray(ArrayObject rawArray, Type targetType)
    {
        var elementType = targetType.IsArray
            ? targetType.GetElementType()!
            : targetType.GetGenericArguments().FirstOrDefault() ?? typeof(object);

        var values = new List<object?>(rawArray.Count);
        for (var i = 0; i < rawArray.Count; i++)
        {
            var value = rawArray.GetValue(i);
            values.Add(value is null ? null : ConvertToTargetType(value, elementType));
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                array.SetValue(values[i], i);
            }

            return array;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var value in values)
        {
            list.Add(value);
        }

        return list;
    }

}
