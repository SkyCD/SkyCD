using System;

namespace SkyCD.Couchbase.Exceptions;

public sealed class DuplicateRepositoryKeyException(Type key)
    : ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));