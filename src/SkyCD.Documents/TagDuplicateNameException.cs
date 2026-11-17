using System;

namespace SkyCD.Documents;

public sealed class TagDuplicateNameException(string name)
    : InvalidOperationException($"A tag named '{name}' already exists. Tag names are unique ignoring case.")
{
}
