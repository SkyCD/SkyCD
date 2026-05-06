using System;

namespace SkyCD.Couchbase.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class Id : Attribute
{
}
