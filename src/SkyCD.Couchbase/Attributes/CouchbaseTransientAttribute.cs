using System;

namespace SkyCD.Couchbase.Attributes;

/// <summary>
/// Marks a computed member that is projected at runtime and must never be written to a Couchbase document.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CouchbaseTransientAttribute : Attribute
{
}
