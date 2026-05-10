using System;
using System.Collections.Generic;
using Couchbase.Lite;
using SkyCD.Couchbase.Models;

namespace SkyCD.Couchbase.Repository;

public interface IRepository<TDocument>
    where TDocument : class, new()
{
    Type DocumentType { get; }

    string CollectionName { get; }

    DocumentPropertyBinding IdProperty { get; }

    Collection Collection { get; }

    void Initialize(Type documentType, string collectionName, Collection collection);

    TDocument? Get(string id);

    TDocument GetOrCreate(string id);

    void Save(string id, TDocument value);

    IReadOnlyList<TDocument> GetAll();
}
