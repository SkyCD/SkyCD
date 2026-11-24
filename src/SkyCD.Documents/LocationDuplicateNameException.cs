using System;

namespace SkyCD.Documents;

public sealed class LocationDuplicateNameException(string name)
    : InvalidOperationException($"A location named '{name}' already exists. Location names are unique ignoring case.")
{
    public string Name { get; } = name;
}
