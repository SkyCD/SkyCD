using System.Reflection;

namespace SkyCD.Couchbase.Models;

public sealed record DocumentPropertyBinding(string Name, PropertyInfo? Property);
