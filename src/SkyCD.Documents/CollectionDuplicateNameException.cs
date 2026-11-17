using System;

namespace SkyCD.Documents;

public sealed class CollectionDuplicateNameException(string name)
    : InvalidOperationException($"A collection named '{name}' already exists.")
{
}
