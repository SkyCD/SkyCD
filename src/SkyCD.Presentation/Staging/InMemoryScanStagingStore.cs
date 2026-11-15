using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Presentation.Staging.Exceptions;

namespace SkyCD.Presentation.Staging;

public sealed class InMemoryScanStagingStore : IStagingStore
{
    private readonly ConcurrentDictionary<string, ScanStagingSession> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<CatalogDocument>> stagedDocs = new(StringComparer.Ordinal);
    private bool isDisposed;

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
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        sessions[sessionId] = session;
        stagedDocs[sessionId] = [];
        return session;
    }

    public ScanStagingSession? GetSession(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public IReadOnlyList<ScanStagingSession> GetSessions()
    {
        ThrowIfDisposed();
        return sessions.Values.ToList();
    }

    public void StageDocuments(string sessionId, IEnumerable<CatalogDocument> documents)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(documents);

        if (!sessions.TryGetValue(sessionId, out var session))
        {
            throw new StagingSessionNotFoundException(sessionId);
        }

        if (session.Status == ScanStagingSessionStatus.Committed)
        {
            throw new StagingSessionAlreadyCommittedException(sessionId);
        }

        if (session.Status != ScanStagingSessionStatus.Active)
        {
            throw new StagingSessionAlreadyClosedException(sessionId);
        }

        var list = stagedDocs.GetOrAdd(sessionId, _ => []);
        lock (list)
        {
            list.AddRange(documents);
            session.DocumentsCount = list.Count;
        }
    }

    public IReadOnlyList<CatalogDocument> GetStagedDocuments(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!sessions.ContainsKey(sessionId))
        {
            throw new StagingSessionNotFoundException(sessionId);
        }

        if (stagedDocs.TryGetValue(sessionId, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }

        return [];
    }

    public void UpdateSession(ScanStagingSession session)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);

        if (!sessions.ContainsKey(session.SessionId))
        {
            throw new StagingSessionNotFoundException(session.SessionId);
        }

        sessions[session.SessionId] = session;
    }

    public void DropSession(string sessionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        sessions.TryRemove(sessionId, out _);
        stagedDocs.TryRemove(sessionId, out _);
    }

    public int CleanupAbandonedSessions(TimeSpan olderThan)
    {
        ThrowIfDisposed();
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var toDrop = sessions.Values
            .Where(s => s.CreatedAtUtc < cutoff || s.Status is ScanStagingSessionStatus.Abandoned or ScanStagingSessionStatus.Cancelled or ScanStagingSessionStatus.Failed)
            .Select(s => s.SessionId)
            .ToList();

        foreach (var id in toDrop)
        {
            DropSession(id);
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
        sessions.Clear();
        stagedDocs.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
