using System;

namespace SkyCD.Documents;

public sealed class CollectionNotFoundException(string collectionId)
    : InvalidOperationException($"Collection '{collectionId}' does not exist.")
{
}
