using System;
using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.Staging;

public interface IStagingStore : IDisposable
{
    ScanStagingSession CreateSession(
        string? sourcePathOrAddress = null,
        string? targetParentId = null,
        string? mediaName = null,
        string? generationId = null);

    ScanStagingSession? GetSession(string sessionId);

    IReadOnlyList<ScanStagingSession> GetSessions();

    void StageDocuments(string sessionId, IEnumerable<CatalogDocument> documents);

    IReadOnlyList<CatalogDocument> GetStagedDocuments(string sessionId);

    void UpdateSession(ScanStagingSession session);

    void DropSession(string sessionId);

    int CleanupAbandonedSessions(TimeSpan olderThan);
}
