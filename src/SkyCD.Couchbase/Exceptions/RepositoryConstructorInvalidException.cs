using System;

namespace SkyCD.Couchbase.Exceptions;

public sealed class RepositoryConstructorInvalidException(Type repositoryType)
    : InvalidOperationException($"Repository type '{repositoryType.FullName}' must have a public parameterless constructor.");
