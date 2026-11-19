using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.UI.Controls.Preview;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the catalog browser's offline preview and typed-metadata surface: what it shows for each stored
/// record state, that browsing never reconnects media or re-runs a decoder, that rapid selection settles on
/// the last entry, and that captured fields reach advanced search without media-specific parsing.
/// </summary>
public sealed class MediaPreviewBrowsingIntegrationTests : IDisposable
{
    private const string MediaRootId = "media-root";
    private const string TrackEntryId = "file-track-1";
    private const string NotesEntryId = "file-notes-1";

    private static readonly byte[] PreviewBytes = [9, 8, 7, 6];

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-preview-browse-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public MediaPreviewBrowsingIntegrationTests()
    {
        Directory.CreateDirectory(databaseDirectory);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Couchbase Lite can release file handles slightly after dispose on some systems.
        }
        catch (UnauthorizedAccessException)
        {
            // Blob files can stay locked briefly after the database is closed on Windows.
        }
    }

    [Fact]
    public async Task CapturedPreviewAndFields_AreBrowsable_WithoutSourceOrPlugins()
    {
        var entry = AudioEntry();

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            catalog.Save(MediaRootId, MediaRoot());
            catalog.Save(entry.Id, entry);

            using var store = MediaCacheStore.Create(database);
            await CaptureAsync(store, entry, catalog);
        }

        // Reopened catalog: the media is gone, no plugin is registered, and nothing may be decoded again.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var resolver = new MediaPreviewResolver(store);
            var reopenedEntry = catalog.GetAll().Single(document => document.Id == TrackEntryId);

            var snapshot = resolver.Resolve(reopenedEntry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal("plugin.preview", snapshot.ProviderId);
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));
            Assert.Equal(PreviewBytes, payload);

            var comments = snapshot.Fields.Single(field => field.Key == "Comments");
            Assert.Equal("captured comment", comments.Value);
            var bitrate = snapshot.Fields.Single(field => field.Key == "Bitrate");
            Assert.Equal(320L, bitrate.Value);
            Assert.Equal("kbps", bitrate.Unit);
            Assert.DoesNotContain(snapshot.Fields, field => field.IsFromStaleRecord);
        }
    }

    [Fact]
    public async Task PreviewViewModel_ShowsCapturedFieldsAsLocalizedRows_WithoutDecoding()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        catalog.Save(MediaRootId, MediaRoot());
        catalog.Save(entry.Id, entry);

        using var store = MediaCacheStore.Create(database);
        await CaptureAsync(store, entry, catalog);

        using var images = new MediaPreviewImageService(new MediaPreviewResolver(store));
        using var preview = new EntryPreviewViewModel(new MediaPreviewResolver(store), images: null);

        await preview.ShowAsync(entry);

        Assert.Equal(MediaPreviewState.Ready, preview.State);
        Assert.Equal(entry.Name, preview.EntryName);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.StateReady), preview.StatusText);

        var bitrate = preview.Fields.Single(row => row.Label == "Bitrate");
        Assert.Contains("320", bitrate.Value, StringComparison.Ordinal);
        Assert.Contains("kbps", bitrate.Value, StringComparison.Ordinal);
        Assert.False(bitrate.IsUserEdited);
        Assert.Null(bitrate.Hint);
    }

    [Fact]
    public async Task UserAnnotation_WinsOverCapturedValue_AndKeepsTheCapturedValueVisible()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        catalog.Save(MediaRootId, MediaRoot());
        catalog.Save(entry.Id, entry);

        using var store = MediaCacheStore.Create(database);
        await CaptureAsync(store, entry, catalog);

        var annotated = WithProperty(entry, "Comments", "my own note");
        catalog.Save(annotated.Id, annotated);

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(catalog.GetAll().Single(document => document.Id == TrackEntryId));
        var comments = snapshot.Fields.Single(field => field.Key == "Comments");

        Assert.Equal(MediaMetadataOrigin.UserEdited, comments.Origin);
        Assert.Equal("my own note", comments.Value);
        Assert.Equal("captured comment", comments.CapturedValue);

        using var preview = new EntryPreviewViewModel(resolver);
        await preview.ShowAsync(catalog.GetAll().Single(document => document.Id == TrackEntryId));

        var row = preview.Fields.Single(field => field.Label == "Comments");
        Assert.Equal("my own note", row.Value);
        Assert.True(row.IsUserEdited);
        Assert.Contains("captured comment", row.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleRecord_StaysBrowsableOffline_AndIsReportedAsOutOfDate()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        SaveArtifactRecord(store, entry.Id, MediaCacheRecordState.Stale, "plugin.preview", "0.9.0");

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(MediaPreviewState.Stale, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
        Assert.True(snapshot.IsStale);
        Assert.True(resolver.TryReadPayload(snapshot, out var payload));
        Assert.Equal(PreviewBytes, payload);
    }

    [Fact]
    public void MissingArtifactBytes_ReportMissingState_InsteadOfFailingNavigation()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        SaveArtifactRecord(store, entry.Id, MediaCacheRecordState.Missing, "plugin.preview", "1.0.0");

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(MediaPreviewState.MissingArtifact, snapshot.State);
        Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
        Assert.False(resolver.TryReadPayload(snapshot, out _));
    }

    [Fact]
    public void UnsupportedEntry_ReportsUnsupportedState()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, [MediaRoot(), entry]);
        store.Save(new MediaCachePayload(new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateUnsupportedId(fingerprint),
            EntryId = entry.Id,
            Fingerprint = fingerprint,
            PluginId = MediaCacheRecord.HostPluginId,
            Capability = MediaCacheRecord.UnsupportedCapability,
            State = MediaCacheRecordState.Unsupported,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow
        }));

        var snapshot = new MediaPreviewResolver(store).Resolve(entry);

        Assert.Equal(MediaPreviewState.Unsupported, snapshot.State);
        Assert.False(snapshot.HasPayload);
    }

    [Fact]
    public async Task FailedExtraction_ReportsFailedStateWithSanitizedReason()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, [MediaRoot(), entry]);
        store.Save(new MediaCachePayload(new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, "plugin.audio", "1.0.0", "Metadata", "fake.audio", 1),
            EntryId = entry.Id,
            Fingerprint = fingerprint,
            PluginId = "plugin.audio",
            PluginVersion = "1.0.0",
            Capability = "Metadata",
            SchemaId = "fake.audio",
            SchemaVersion = 1,
            State = MediaCacheRecordState.Failed,
            FailureReason = "failed",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow
        }));

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(MediaPreviewState.Failed, snapshot.State);
        Assert.Equal("failed", snapshot.FailureReason);

        using var preview = new EntryPreviewViewModel(resolver);
        await preview.ShowAsync(entry);

        Assert.Equal(MediaPreviewState.Failed, preview.State);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.StateFailed), preview.StatusText);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.PreviewFailed), preview.StateMessage);
    }

    [Fact]
    public void EntryWithoutCapturedResults_ReportsUnavailable()
    {
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);

        var snapshot = new MediaPreviewResolver(store).Resolve(AudioEntry());

        Assert.Equal(MediaPreviewState.Unavailable, snapshot.State);
        Assert.False(snapshot.HasFields);
        Assert.False(snapshot.HasPayload);
    }

    [Fact]
    public void NoSelection_ResolvesToTheNoSelectionState()
    {
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        var resolver = new MediaPreviewResolver(store);

        Assert.Equal(MediaPreviewState.NoSelection, resolver.Resolve(null).State);
        Assert.Equal(EntryPreviewSnapshot.NoSelection, resolver.Resolve(new CatalogDocument { Id = " " }));
    }

    [Fact]
    public void OversizedArtifact_IsReportedInsteadOfDecoded()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database, new MediaCacheRetentionPolicy(MaxArtifactBytesPerRecord: 64));
        SaveArtifactRecord(store, entry.Id, MediaCacheRecordState.Captured, "plugin.preview", "1.0.0");

        var resolver = new MediaPreviewResolver(store, new MediaPreviewDisplayPolicy(MaxDecodedArtifactBytes: 2));
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(MediaPreviewState.TooLarge, snapshot.State);
        Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
    }

    [Fact]
    public void TextArtifact_IsDecodedAsBoundedText()
    {
        var entry = NotesEntry();
        using var database = OpenDatabase();
        using var store = MediaCacheStore.Create(database);
        var body = Encoding.UTF8.GetBytes("line one\nline two\nline three");
        SaveArtifactRecord(store, entry.Id, MediaCacheRecordState.Captured, "plugin.text", "1.0.0",
            contentType: "text/plain", bytes: body);

        var resolver = new MediaPreviewResolver(store, new MediaPreviewDisplayPolicy(MaxTextPreviewCharacters: 8));
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(PreviewPayloadKind.Text, snapshot.PayloadKind);
        Assert.Equal("line one", snapshot.TextPreview);
        Assert.True(snapshot.IsTextPreviewTruncated);
    }

    [Fact]
    public async Task RapidSelection_KeepsOnlyTheLastEntry()
    {
        var entries = Enumerable.Range(0, 25)
            .Select(index => new CatalogDocument
            {
                Id = $"file-{index:D3}",
                Name = $"track-{index:D3}.mp3",
                ParentId = MediaRootId,
                Type = CatalogDocumentType.File,
                Size = 64
            })
            .ToArray();

        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        catalog.Save(MediaRootId, MediaRoot());
        foreach (var entry in entries)
        {
            catalog.Save(entry.Id, entry);
        }

        using var store = MediaCacheStore.Create(database);
        foreach (var entry in entries)
        {
            SaveArtifactRecord(store, entry.Id, MediaCacheRecordState.Captured, "plugin.preview", "1.0.0");
        }

        using var preview = new EntryPreviewViewModel(new MediaPreviewResolver(store));

        // Fire every selection without awaiting, exactly as a fast arrow-key walk through the browser does.
        foreach (var entry in entries)
        {
            preview.Show(entry);
        }

        var last = entries[^1];
        await preview.ShowAsync(last);

        Assert.Equal(last.Id, preview.CurrentSnapshot?.EntryId);
        Assert.Equal(last.Name, preview.EntryName);
        Assert.Equal(MediaPreviewState.Ready, preview.State);
    }

    [Fact]
    public async Task Search_MatchesCapturedFields_WithoutMediaSpecificParsing()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        catalog.Save(MediaRootId, MediaRoot());
        catalog.Save(entry.Id, entry);
        catalog.Save(NotesEntryId, NotesEntry());

        using var store = MediaCacheStore.Create(database);
        await CaptureAsync(store, entry, catalog);

        var provider = new MediaCacheSearchFieldProvider(store);
        var documents = catalog.GetAll();

        var byFieldValue = CatalogFindService.Search(documents, "captured comment", provider);
        var matched = Assert.Single(byFieldValue);
        Assert.Equal(TrackEntryId, matched.Document.Id);
        Assert.Equal("Comments", matched.MatchedField);
        Assert.True(matched.HasMatchedField);

        // The same query finds nothing without the provider, proving the match came from stored metadata.
        Assert.Empty(CatalogFindService.Search(documents, "captured comment"));

        // Path matching keeps working unchanged.
        Assert.Equal(3, CatalogFindService.Search(documents, "Mixtape", provider).Count);

        using var browser = MainWindowViewModel.CreateForInMemoryCatalog(documents, mediaCacheStore: store);
        browser.FindCatalogCommand.Execute(null);
        browser.SearchWholeCatalog = true;
        browser.QuickFindQuery = "CAPTURED COMMENT";
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (browser.QuickFindResults.Count == 0)
        {
            await Task.Delay(10, timeout.Token);
        }

        var quickMatch = Assert.Single(browser.QuickFindResults);
        Assert.Equal(TrackEntryId, quickMatch.Document.Id);
        Assert.Equal("Comments", quickMatch.MatchedField);
        Assert.False(browser.IsDirtyDocument);
    }

    [Fact]
    public async Task BrowsingCachedEntries_NeverOpensTheOriginalSource()
    {
        var entry = AudioEntry();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        catalog.Save(MediaRootId, MediaRoot());
        catalog.Save(entry.Id, entry);

        using var store = MediaCacheStore.Create(database);
        await CaptureAsync(store, entry, catalog);

        // The catalog points at a path that does not exist; browsing must not care.
        var disconnected = WithProperty(entry, "FullName", Path.Combine(databaseDirectory, "gone", "track.mp3"));
        Assert.False(File.Exists(disconnected.Properties["FullName"]!.ToString()));

        var snapshot = new MediaPreviewResolver(store).Resolve(disconnected);

        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
    }

    private static async Task CaptureAsync(
        IMediaCacheStore store,
        CatalogDocument entry,
        CatalogDocumentRepository catalog)
    {
        var scheduler = CreateScheduler(store);

        var outcome = await scheduler.ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(entry, OpenSource, catalog.GetAll()));

        Assert.True(outcome.Published);
        Assert.All(outcome.Records, record => Assert.Equal(MediaCacheRecordState.Captured, record.State));
    }

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(new MediaProvider[]
        {
            new("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            new("plugin.preview", "1.0.0", new FakePreviewCapability())
        }), store);

    private static Task<Stream> OpenSource(CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(new byte[64]));

    private Database OpenDatabase() =>
        new("preview-browsing-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument MediaRoot() => new()
    {
        Id = MediaRootId,
        Name = "Mixtape",
        ParentId = "library",
        Type = CatalogDocumentType.Media
    };

    private static CatalogDocument AudioEntry() => new()
    {
        Id = TrackEntryId,
        Name = "track.mp3",
        ParentId = MediaRootId,
        Type = CatalogDocumentType.File,
        Size = 64,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Music\Mixtape\track.mp3",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static CatalogDocument NotesEntry() => new()
    {
        Id = NotesEntryId,
        Name = "notes.txt",
        ParentId = MediaRootId,
        Type = CatalogDocumentType.File,
        Size = 28
    };

    private static CatalogDocument WithProperty(CatalogDocument source, string key, object value)
    {
        var properties = new PropertiesCollection();
        foreach (var (existingKey, existingValue) in source.Properties)
        {
            properties[existingKey] = existingValue;
        }

        properties[key] = value;

        return new CatalogDocument
        {
            Id = source.Id,
            Name = source.Name,
            ParentId = source.ParentId,
            Type = source.Type,
            Size = source.Size,
            ChildrenCount = source.ChildrenCount,
            ContentsState = source.ContentsState,
            Status = source.Status,
            Properties = properties
        };
    }

    private static void SaveArtifactRecord(
        IMediaCacheStore store,
        string entryId,
        MediaCacheRecordState state,
        string pluginId,
        string pluginVersion,
        string contentType = "image/png",
        byte[]? bytes = null)
    {
        var payloadBytes = bytes ?? PreviewBytes;
        var fingerprint = $"fingerprint-{entryId}";
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, pluginId, pluginVersion, "Preview", "fake.preview", 1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = pluginId,
            PluginVersion = pluginVersion,
            Capability = "Preview",
            SchemaId = "fake.preview",
            SchemaVersion = 1,
            State = state,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
            SourceLength = payloadBytes.Length,
            Artifacts =
            [
                new MediaArtifactReference
                {
                    Key = "preview:0",
                    ContentType = contentType,
                    Length = payloadBytes.Length,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payloadBytes))
                        .ToLowerInvariant()
                }
            ]
        }.WithRecalculatedSizes();

        store.Save(new MediaCachePayload(record, new Dictionary<string, byte[]> { ["preview:0"] = payloadBytes }));
    }

    private sealed class FakeAudioMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new(
            [".mp3"],
            ["audio/mpeg"],
            new MediaMetadataSchema(
                "fake.audio",
                1,
                [new("Comments", MediaMetadataType.Text), new("Bitrate", MediaMetadataType.Integer, "kbps")]));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaExtractionResult(
            [
                new("Comments", MediaMetadataType.Text, Text: "captured comment"),
                new("Bitrate", MediaMetadataType.Integer, Integer: 320)
            ]));
    }

    private sealed class FakePreviewCapability : IMediaPreviewCapability
    {
        public MediaDescriptor Descriptor => new([".mp3"], [], new MediaMetadataSchema("fake.preview", 1, []));

        public Task<MediaPreviewResult> GeneratePreviewAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaPreviewResult([new MediaPreviewArtifact("image/png", PreviewBytes)]));
    }
}
