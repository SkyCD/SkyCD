using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Couchbase.Lite;

namespace SkyCD.Couchbase.Decorators;

internal sealed class MutableDictionaryDecorator(IMutableDictionary dictionary) : IMutableDictionary
{
    public int Count => dictionary.Count;

    public ICollection<string> Keys => dictionary.Keys;

    IFragment IDictionaryFragment.this[string key] => (IFragment)dictionary[key];
    IMutableFragment IMutableDictionaryFragment.this[string key] => dictionary[key];
    IMutableFragment IMutableDictionary.this[string key] => dictionary[key];

    public void WriteObject(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var properties = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(source);
            SetMappedValue(property.Name, value);
        }
    }

    private void SetMappedValue(string key, object? value)
    {
        switch (value)
        {
            case null:
                dictionary.SetValue(key, null);
                return;
            case string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                dictionary.SetValue(key, value);
                return;
            case Enum enumValue:
                dictionary.SetString(key, enumValue.ToString());
                return;
            case DateTime dateTime:
                dictionary.SetDate(key, new DateTimeOffset(dateTime));
                return;
            case DateTimeOffset dateTimeOffset:
                dictionary.SetDate(key, dateTimeOffset);
                return;
            case IEnumerable enumerable and not string:
                {
                    var array = new MutableArrayObject();
                    foreach (var item in enumerable)
                    {
                        array.AddValue(ToMutableValue(item));
                    }

                    dictionary.SetArray(key, array);
                    return;
                }
        }

        var nestedDictionary = new MutableDictionaryObject();
        new MutableDictionaryDecorator(nestedDictionary).WriteObject(value);
        dictionary.SetDictionary(key, nestedDictionary);
    }

    private static object? ToMutableValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return value;
            case Enum enumValue:
                return enumValue.ToString();
            case DateTime dateTime:
                return new DateTimeOffset(dateTime);
            case DateTimeOffset dateTimeOffset:
                return dateTimeOffset;
            case IEnumerable enumerable and not string:
                {
                    var array = new MutableArrayObject();
                    foreach (var item in enumerable)
                    {
                        array.AddValue(ToMutableValue(item));
                    }

                    return array;
                }
        }

        var nestedDictionary = new MutableDictionaryObject();
        new MutableDictionaryDecorator(nestedDictionary).WriteObject(value);
        return nestedDictionary;
    }

    public bool Contains(string key)
    {
        return dictionary.Contains(key);
    }

    ArrayObject IDictionaryObject.GetArray(string key) => dictionary.GetArray(key)!;
    MutableArrayObject IMutableDictionary.GetArray(string key) => dictionary.GetArray(key)!;

    public Blob GetBlob(string key)
    {
        return dictionary.GetBlob(key)!;
    }

    public bool GetBoolean(string key)
    {
        return dictionary.GetBoolean(key);
    }

    public DateTimeOffset GetDate(string key)
    {
        return dictionary.GetDate(key);
    }

    DictionaryObject IDictionaryObject.GetDictionary(string key) => dictionary.GetDictionary(key)!;
    MutableDictionaryObject IMutableDictionary.GetDictionary(string key) => dictionary.GetDictionary(key)!;

    public double GetDouble(string key)
    {
        return dictionary.GetDouble(key);
    }

    public float GetFloat(string key)
    {
        return dictionary.GetFloat(key);
    }

    public int GetInt(string key)
    {
        return dictionary.GetInt(key);
    }

    public long GetLong(string key)
    {
        return dictionary.GetLong(key);
    }

    public string? GetString(string key)
    {
        return dictionary.GetString(key);
    }

    public object? GetValue(string key)
    {
        return dictionary.GetValue(key);
    }

    public Dictionary<string, object?> ToDictionary()
    {
        return dictionary.ToDictionary();
    }

    public IMutableDictionary Remove(string key)
    {
        dictionary.Remove(key);
        return this;
    }

    public IMutableDictionary SetArray(string key, ArrayObject? value)
    {
        dictionary.SetArray(key, value);
        return this;
    }

    public IMutableDictionary SetBlob(string key, Blob? value)
    {
        dictionary.SetBlob(key, value);
        return this;
    }

    public IMutableDictionary SetBoolean(string key, bool value)
    {
        dictionary.SetBoolean(key, value);
        return this;
    }

    public IMutableDictionary SetData(IDictionary<string, object?> properties)
    {
        dictionary.SetData(properties);
        return this;
    }

    public IMutableDictionary SetDate(string key, DateTimeOffset value)
    {
        dictionary.SetDate(key, value);
        return this;
    }

    public IMutableDictionary SetDictionary(string key, DictionaryObject? value)
    {
        dictionary.SetDictionary(key, value);
        return this;
    }

    public IMutableDictionary SetDouble(string key, double value)
    {
        dictionary.SetDouble(key, value);
        return this;
    }

    public IMutableDictionary SetFloat(string key, float value)
    {
        dictionary.SetFloat(key, value);
        return this;
    }

    public IMutableDictionary SetInt(string key, int value)
    {
        dictionary.SetInt(key, value);
        return this;
    }

    public IMutableDictionary SetJSON(string json)
    {
        dictionary.SetJSON(json);
        return this;
    }

    public IMutableDictionary SetLong(string key, long value)
    {
        dictionary.SetLong(key, value);
        return this;
    }

    public IMutableDictionary SetString(string key, string? value)
    {
        dictionary.SetString(key, value);
        return this;
    }

    public IMutableDictionary SetValue(string key, object? value)
    {
        dictionary.SetValue(key, value);
        return this;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<string, object?>>)dictionary).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)dictionary).GetEnumerator();
    }
}
