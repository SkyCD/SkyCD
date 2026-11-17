using System;

namespace SkyCD.Documents;

public sealed class CollectionNameRequiredException()
    : InvalidOperationException("A collection name is required.")
{
}
