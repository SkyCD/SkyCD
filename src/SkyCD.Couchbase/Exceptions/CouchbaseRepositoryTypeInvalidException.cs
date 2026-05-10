using System;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Couchbase.Exceptions;

public sealed class CouchbaseRepositoryTypeInvalidException()
    : ArgumentException("Repository type must implement IRepository<TDocument>.", "repositoryType");
