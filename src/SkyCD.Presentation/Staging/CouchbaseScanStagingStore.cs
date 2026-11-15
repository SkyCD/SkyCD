using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Presentation.Staging.Exceptions;

namespace SkyCD.Presentation.Staging;

public sealed class CouchbaseScanStagingStore : IStagingStore
{
    private const string SessionsCollectionName = "staging_sessions";
    private const string DocumentsCollectionName = "staging_documents";

    private readonly Database database;
    private readonly Collection sessionsCollection;
    private readonly Collection documentsCollection;
    private bool isDisposed;

    public CouchbaseScanStagingStore(Database database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        sessionsCollection = database.GetCollection(SessionsCollectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(SessionsCollectionName, Collection.DefaultScopeName);
        documentsCollection = database.GetCollection(DocumentsCollectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(DocumentsCollectionName, Collection.DefaultScopeName);
    }

    public ScanStagingSession CreateSession(
        string? sourcePathOrAddress = null,
        string? targetParentId = null,
        string? mediaName = null,
        string? generationId = null)
    {
        ThrowIfDisposed();

        var sessionId = $"stage-{Guid.NewGuid():N}";
        var genId = generationId ?? $"gen-{Guid.NewGuid():N}";
        var session = new ScanStagingSession
        {
            SessionId = sessionId,
            GenerationId = genId,
            SourcePathOrAddress = sourcePathOrAddress,
            TargetParentId = targetParentId,
            MediaName = mediaName,
            Status = ScanStagingSessionStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DocumentsCount = 0
        };

        SaveSessionDocument(session);
        return session;
    }

    public ScanStagingSession? GetSession(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        using var doc = sessionsCollection.GetDocument(sessionId);
        if (doc is null)
        {
            return null;
        }

        return doc.FromDocument<ScanStagingSession>();
    }

    public IReadOnlyList<ScanStagingSession> GetSessions()
    {
        ThrowIfDisposed();

        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(sessionsCollection));

        using var results = query.Execute();
        var list = new List<ScanStagingSession>();

        foreach (var row in results)
        {
            var dict = row.GetDictionary(sessionsCollection.Name);
            if (dict is null)
            {
                continue;
            }

            using var doc = new MutableDocument(dict.ToDictionary());
            var session = doc.FromDocument<ScanStagingSession>();
            if (session is not null)
            {
                list.Add(session);
            }
        }

        return list;
    }

    public void StageDocuments(string sessionId, IEnumerable<CatalogDocument> documents)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(documents);

        var session = GetSession(sessionId)
            ?? throw new StagingSessionNotFoundException(sessionId);

        if (session.Status == ScanStagingSessionStatus.Committed)
        {
            throw new StagingSessionAlreadyCommittedException(sessionId);
        }

        if (session.Status != ScanStagingSessionStatus.Active)
        {
            throw new StagingSessionAlreadyClosedException(sessionId);
        }

        var docList = documents as IReadOnlyList<CatalogDocument> ?? documents.ToList();
        if (docList.Count == 0)
        {
            return;
        }

        database.InBatch(() =>
        {
            var counter = session.DocumentsCount;
            foreach (var doc in docList)
            {
                var stagedDocId = $"{sessionId}:{counter++:D8}:{doc.Id}";
                using var stagedMutable = doc.ToMutableDocument(stagedDocId);
                stagedMutable.SetString("SessionId", sessionId);
                stagedMutable.SetString("GenerationId", session.GenerationId);
                stagedMutable.SetInt("Sequence", counter);

                documentsCollection.Save(stagedMutable);
            }

            session.DocumentsCount += docList.Count;
            SaveSessionDocument(session);
        });
    }

    public IReadOnlyList<CatalogDocument> GetStagedDocuments(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = GetSession(sessionId)
            ?? throw new StagingSessionNotFoundException(sessionId);

        using var query = QueryBuilder
            .Select(SelectResult.All(), SelectResult.Property("Sequence"))
            .From(DataSource.Collection(documentsCollection))
            .Where(Expression.Property("SessionId").EqualTo(Expression.String(sessionId)))
            .OrderBy(Ordering.Property("Sequence").Ascending());

        using var results = query.Execute();
        var list = new List<CatalogDocument>();

        foreach (var row in results)
        {
            var dict = row.GetDictionary(documentsCollection.Name);
            if (dict is null)
            {
                continue;
            }

            using var doc = new MutableDocument(dict.ToDictionary());
            var catalogDoc = doc.FromDocument<CatalogDocument>();
            if (catalogDoc is not null)
            {
                list.Add(catalogDoc);
            }
        }

        return list;
    }

    public void UpdateSession(ScanStagingSession session)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);

        using var existing = sessionsCollection.GetDocument(session.SessionId);
        if (existing is null)
        {
            throw new StagingSessionNotFoundException(session.SessionId);
        }

        SaveSessionDocument(session);
    }

    public void DropSession(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        database.InBatch(() =>
        {
            using var sessionDoc = sessionsCollection.GetDocument(sessionId);
            if (sessionDoc is not null)
            {
                sessionsCollection.Delete(sessionDoc);
            }

            using var query = QueryBuilder
                .Select(SelectResult.Expression(Meta.ID))
                .From(DataSource.Collection(documentsCollection))
                .Where(Expression.Property("SessionId").EqualTo(Expression.String(sessionId)));

            using var results = query.Execute();
            foreach (var row in results)
            {
                var id = row.GetString("id");
                if (!string.IsNullOrEmpty(id))
                {
                    using var doc = documentsCollection.GetDocument(id);
                    if (doc is not null)
                    {
                        documentsCollection.Delete(doc);
                    }
                }
            }
        });
    }

    public int CleanupAbandonedSessions(TimeSpan olderThan)
    {
        ThrowIfDisposed();
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var allSessions = GetSessions();
        var toDrop = allSessions
            .Where(s => s.CreatedAtUtc < cutoff || s.Status is ScanStagingSessionStatus.Abandoned or ScanStagingSessionStatus.Cancelled or ScanStagingSessionStatus.Failed)
            .Select(s => s.SessionId)
            .ToList();

        foreach (var sessionId in toDrop)
        {
            DropSession(sessionId);
        }

        return toDrop.Count;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
    }

    private void SaveSessionDocument(ScanStagingSession session)
    {
        using var doc = session.ToMutableDocument(session.SessionId);
        sessionsCollection.Save(doc);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
