using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.TestSupport;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.Audio.Models;

namespace SkyCD.Plugin.Metadata.Audio.Tests;

/// <summary>
/// End-to-end coverage for the audio plugin inside the host pipeline: provider selection, host resource
/// budgets, cancellation, failure isolation, assembly discovery without a manifest, and offline retrieval of
/// captured tags and cover art after the database is reopened with the media gone and the plugin unloaded.
/// </summary>
public sealed class AudioPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Metadata.Audio";
    private const string MediaRootId = "media-root";
    private const string TrackEntryId = "file-track-1";

    private readonly TestWorkspace workspace = new();

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    private string databaseDirectory => workspace.Root;

    public AudioPluginIntegrationTests()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        workspace.Dispose();
    }

    [Fact]
    public async Task Host_RunsBothCapabilitiesForEverySupportedContainer()
    {
        foreach (var (audio, extension, contentType) in SupportedSources())
        {
            var results = await CreateManager().ProcessAsync(
                new MediaItem(extension, contentType, audio.Length),
                _ => Task.FromResult<Stream>(new MemoryStream(audio)));

            var metadata = results.Single(result => result.Metadata is not null);
            var preview = results.Single(result => result.Preview is not null);

            Assert.False(metadata.Failed);
            Assert.False(preview.Failed);
            Assert.Equal("skycd.audio.metadata", metadata.Schema!.Id);
            Assert.Equal("skycd.audio.preview", preview.Schema!.Id);
            Assert.NotEmpty(preview.Preview!.Artifacts);
        }
    }

    [Fact]
    public void Host_ClaimsOnlyTheDocumentedAudioTypes()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".mp3", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".FLAC", "audio/flac", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".ogg", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".m4a", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", "image/jpeg", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));

        // A plain .mp4 belongs to the video plugin; only the audio MIME type claims it here.
        Assert.Empty(manager.ResolveProviders(new MediaItem(".mp4", "video/mp4", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".mp4", "audio/mp4", 10)));
    }

    [Fact]
    public async Task MalformedAudio_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var audio = AudioFixtures.NotAudio();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".mp3", "audio/mpeg", audio.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(audio)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
        Assert.All(results, result => Assert.Null(result.Preview));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsAudioLargerThanTheBudget()
    {
        var audio = AudioFixtures.Mp3(frameCount: 40);
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 32));

        var results = await manager.ProcessAsync(
            new MediaItem(".mp3", "audio/mpeg", audio.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(audio)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task CapturedTagsAndCoverArt_SurviveReopen_WithoutThePluginOrTheSource()
    {
        var audio = AudioFixtures.Mp3(frameCount: 100);

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, audio.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(audio)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == TrackEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));
            Assert.Equal(AudioFixtures.JpegBytes, payload);

            Assert.Equal("Test Title", FieldText(snapshot, AudioSchema.Title));
            Assert.Equal("Test Artist", FieldText(snapshot, AudioSchema.Artist));
            Assert.Equal("Test Album", FieldText(snapshot, AudioSchema.Album));
            Assert.Equal("Rock", FieldText(snapshot, AudioSchema.Genre));
            Assert.Equal(3L, FieldInteger(snapshot, AudioSchema.TrackNumber));
            Assert.Equal(12L, FieldInteger(snapshot, AudioSchema.TrackCount));
            Assert.Equal(2021L, FieldInteger(snapshot, AudioSchema.Year));
            Assert.Equal(44100L, FieldInteger(snapshot, AudioSchema.SampleRate));
            Assert.Equal(128L, FieldInteger(snapshot, AudioSchema.Bitrate));
            Assert.Equal(Math.Round(AudioFixtures.MpegDuration(100), 3), FieldNumber(snapshot, AudioSchema.Duration));
            Assert.Equal(true, Field(snapshot, AudioSchema.HasCoverArt).Value);
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var audio = AudioFixtures.Flac();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, audio.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(audio)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsEveryCapability_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AudioMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Audio Tags, Cover Art and Audio CDs", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaTargetMetadataCapability);
        Assert.Contains(plugin.Capabilities,
            capability => capability is SkyCD.SDK.Capabilities.Configuration.IPluginConfigurationCapability);
        Assert.Equal(4, plugin.Capabilities.Count);
        Assert.Equal("mp3", plugin.PrimaryExtension);
    }

    private static IEnumerable<(byte[] Audio, string Extension, string ContentType)> SupportedSources()
    {
        yield return (AudioFixtures.Mp3(), ".mp3", "audio/mpeg");
        yield return (AudioFixtures.Flac(), ".flac", "audio/flac");
        yield return (AudioFixtures.OggVorbis(), ".ogg", "audio/ogg");
        yield return (AudioFixtures.M4a(), ".m4a", "audio/mp4");
    }

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static double FieldNumber(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is double value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not a number.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new AudioMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new AudioCoverArtPlugin())
        }, limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new AudioMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new AudioCoverArtPlugin())
        }), store);

    private Database OpenDatabase() =>
        new("audio-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument SeedCatalog(CatalogDocumentRepository repository, long entrySize)
    {
        var root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Mixtape",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = TrackEntryId,
            Name = "track.mp3",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Music\Mixtape\track.mp3",
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }

}
