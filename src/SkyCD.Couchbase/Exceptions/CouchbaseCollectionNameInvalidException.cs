using System;

namespace SkyCD.Couchbase.Exceptions;

public sealed class CouchbaseCollectionNameInvalidException()
    : ArgumentException("Collection name cannot be null or whitespace.", "collectionName");