namespace SkyCD.Couchbase.Repository;

public sealed class DefaultRepository<TDocument> : RepositoryBase<TDocument>
    where TDocument : class, new()
{
}
