using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Core.UndoRedo;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Staging.Exceptions;
using SkyCD.Presentation.UndoRedo.Actions;

namespace SkyCD.Presentation.Staging;

public sealed class CatalogScanPublisher(CatalogEntryMerger? entryMerger = null)
{
    private readonly CatalogEntryMerger entryMerger = entryMerger ?? new CatalogEntryMerger();

    public ScanStagingSession StartSession(
        IStagingStore store,
        string? sourcePathOrAddress = null,
        string? targetParentId = null,
        string? mediaName = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.CreateSession(sourcePathOrAddress, targetParentId, mediaName);
    }

    public void StageDocuments(
        IStagingStore store,
        string sessionId,
        IEnumerable<CatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(documents);

        store.StageDocuments(sessionId, documents);
    }

    public ScanPublishResult Publish(
        IStagingStore store,
        string sessionId,
        CatalogDocumentRepository repository,
        ScanPublishOptions? options = null,
        UndoRedoHistory? undoHistory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(repository);

        var session = store.GetSession(sessionId)
            ?? throw new StagingSessionNotFoundException(sessionId);

        if (session.Status == ScanStagingSessionStatus.Committed)
        {
            throw new StagingSessionAlreadyCommittedException(sessionId);
        }

        if (session.Status is ScanStagingSessionStatus.Cancelled or ScanStagingSessionStatus.Abandoned)
        {
            throw new StagingSessionAlreadyClosedException(sessionId);
        }

        options ??= new ScanPublishOptions();
        var stagedDocs = store.GetStagedDocuments(sessionId);

        var result = PublishDocumentsInternal(
            stagedDocs,
            session.GenerationId,
            repository,
            options,
            undoHistory);

        session.Status = ScanStagingSessionStatus.Committed;
        session.IsIncomplete = result.IsIncomplete;
        session.ErrorSummary = result.ErrorSummary;
        store.UpdateSession(session);
        store.DropSession(sessionId);

        return result;
    }

    public ScanPublishResult PublishDirect(
        IReadOnlyList<CatalogDocument> documents,
        CatalogDocumentRepository repository,
        ScanPublishOptions? options = null,
        UndoRedoHistory? undoHistory = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(repository);

        options ??= new ScanPublishOptions();
        var generationId = $"gen-{Guid.NewGuid():N}";

        return PublishDocumentsInternal(
            documents,
            generationId,
            repository,
            options,
            undoHistory);
    }

    public ScanPublishResult? CancelSession(
        IStagingStore store,
        string sessionId,
        bool keepPartial,
        CatalogDocumentRepository? repository = null,
        ScanPublishOptions? options = null,
        UndoRedoHistory? undoHistory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = store.GetSession(sessionId)
            ?? throw new StagingSessionNotFoundException(sessionId);

        if (session.Status == ScanStagingSessionStatus.Committed)
        {
            throw new StagingSessionAlreadyCommittedException(sessionId);
        }

        if (keepPartial && repository is not null)
        {
            options = new ScanPublishOptions
            {
                KeepPartialOnFailureOrCancel = true,
                IsIncomplete = true,
                ErrorSummary = options?.ErrorSummary ?? "Scan cancelled by user",
                BatchSize = options?.BatchSize ?? 1000,
                PreserveUserMetadata = options?.PreserveUserMetadata ?? true
            };

            return Publish(store, sessionId, repository, options, undoHistory);
        }

        session.Status = ScanStagingSessionStatus.Cancelled;
        session.ErrorSummary = "Scan cancelled by user";
        store.UpdateSession(session);
        store.DropSession(sessionId);
        return null;
    }

    public ScanPublishResult? FailSession(
        IStagingStore store,
        string sessionId,
        string errorSummary,
        bool keepPartial,
        CatalogDocumentRepository? repository = null,
        ScanPublishOptions? options = null,
        UndoRedoHistory? undoHistory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = store.GetSession(sessionId)
            ?? throw new StagingSessionNotFoundException(sessionId);

        if (session.Status == ScanStagingSessionStatus.Committed)
        {
            throw new StagingSessionAlreadyCommittedException(sessionId);
        }

        if (keepPartial && repository is not null)
        {
            options = new ScanPublishOptions
            {
                KeepPartialOnFailureOrCancel = true,
                IsIncomplete = true,
                ErrorSummary = errorSummary,
                BatchSize = options?.BatchSize ?? 1000,
                PreserveUserMetadata = options?.PreserveUserMetadata ?? true
            };

            return Publish(store, sessionId, repository, options, undoHistory);
        }

        session.Status = ScanStagingSessionStatus.Failed;
        session.ErrorSummary = errorSummary;
        store.UpdateSession(session);
        store.DropSession(sessionId);
        return null;
    }

    public void AbandonSession(IStagingStore store, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = store.GetSession(sessionId);
        if (session is not null)
        {
            session.Status = ScanStagingSessionStatus.Abandoned;
            store.UpdateSession(session);
        }

        store.DropSession(sessionId);
    }

    private ScanPublishResult PublishDocumentsInternal(
        IReadOnlyList<CatalogDocument> documents,
        string generationId,
        CatalogDocumentRepository repository,
        ScanPublishOptions options,
        UndoRedoHistory? undoHistory)
    {
        if (documents.Count == 0)
        {
            return new ScanPublishResult
            {
                GenerationId = generationId,
                PublishedDocumentsCount = 0,
                InsertedDocumentsCount = 0,
                UpdatedDocumentsCount = 0,
                IsIncomplete = options.IsIncomplete,
                ErrorSummary = options.ErrorSummary,
                PublishedDocuments = []
            };
        }

        var existingDocuments = repository.GetAll();
        var existingById = existingDocuments.ToDictionary(d => d.Id, StringComparer.Ordinal);

        var mergedDocuments = entryMerger.Merge(
            documents,
            existingDocuments,
            preserveUserMetadata: options.PreserveUserMetadata,
            isIncomplete: options.IsIncomplete,
            errorSummary: options.ErrorSummary);

        var inserted = new List<CatalogDocument>();
        var updated = new List<(CatalogDocument Previous, CatalogDocument Updated)>();

        foreach (var doc in mergedDocuments)
        {
            if (existingById.TryGetValue(doc.Id, out var prev))
            {
                updated.Add((prev, doc));
            }
            else
            {
                inserted.Add(doc);
            }
        }

        try
        {
            var batchSize = Math.Max(1, options.BatchSize);
            for (var i = 0; i < mergedDocuments.Count; i += batchSize)
            {
                var chunk = mergedDocuments.Skip(i).Take(batchSize).ToList();
                repository.Collection.Database.InBatch(() =>
                {
                    foreach (var doc in chunk)
                    {
                        repository.Save(doc.Id, doc);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            throw new CatalogScanPublicationException(generationId, ex.Message);
        }

        if (undoHistory is not null && (inserted.Count > 0 || updated.Count > 0))
        {
            var action = new PublishScanAction(
                repository,
                inserted,
                updated,
                displayName: options.IsIncomplete ? "Publish Scan (Partial)" : "Publish Scan");
            undoHistory.PushAction(action);
        }

        return new ScanPublishResult
        {
            GenerationId = generationId,
            RootDocumentId = mergedDocuments.FirstOrDefault()?.Id,
            PublishedDocumentsCount = mergedDocuments.Count,
            InsertedDocumentsCount = inserted.Count,
            UpdatedDocumentsCount = updated.Count,
            IsIncomplete = options.IsIncomplete,
            ErrorSummary = options.ErrorSummary,
            PublishedDocuments = mergedDocuments
        };
    }
}
