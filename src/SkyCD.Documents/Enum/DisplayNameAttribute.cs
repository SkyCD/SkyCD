using System;

namespace SkyCD.Documents.Enum;

[AttributeUsage(AttributeTargets.Field)]
public sealed class DisplayNameAttribute(string displayName) : Attribute
{
    public string Value { get; } = displayName;
}