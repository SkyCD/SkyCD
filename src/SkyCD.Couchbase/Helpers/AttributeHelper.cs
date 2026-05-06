using System;
using System.Reflection;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Helpers;

internal static class AttributeHelper
{
    public static DocumentPropertyBinding ResolveStringPropertyWithAttributeOrDefault(
        Type documentType,
        Type attributeType,
        string defaultPropertyName)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        ArgumentNullException.ThrowIfNull(attributeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPropertyName);

        foreach (var property in documentType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType == typeof(string) &&
                property.IsDefined(attributeType, inherit: true))
            {
                return new DocumentPropertyBinding(property.Name, property);
            }
        }

        var fallback = documentType.GetProperty(defaultPropertyName, BindingFlags.Instance | BindingFlags.Public);
        var fallbackProperty = fallback?.PropertyType == typeof(string) ? fallback : null;
        return new DocumentPropertyBinding(defaultPropertyName, fallbackProperty);
    }
}
