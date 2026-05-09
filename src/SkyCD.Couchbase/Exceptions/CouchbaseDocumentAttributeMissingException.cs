using System;

namespace SkyCD.Couchbase.Exceptions;

public sealed class CouchbaseDocumentAttributeMissingException(Type documentType)
    : InvalidOperationException($"Type '{documentType.FullName}' must be annotated with [CouchbaseDocument(\"collection\")].");
