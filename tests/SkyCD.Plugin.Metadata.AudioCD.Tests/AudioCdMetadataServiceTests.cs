using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.TestSupport;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.Metadata.AudioCD.Tests;

public sealed class AudioCdMetadataServiceTests
{
    [Fact]
    public void FixedDrives_AreNotTreatedAsAudioCds()
    {
        var documents = new[]
        {
            Root("media-1", @"C:\", "Fixed"),
            File("file-1", "readme.txt", @"C:\readme.txt", "media-1")
        };

        Assert.Empty(AudioCdMetadataService.FindDiscRoots(documents));
    }

    [Fact]
    public void OpticalDriveRoot_IsFound_AndVirtualTrackFilesImplyTheRoot()
    {
        using var workspace = new TestWorkspace();
        var opticalRoot = workspace.GetPath("skycd-audio-cd-disc") + Path.DirectorySeparatorChar;
        var trackRoot = workspace.GetPath("skycd-audio-cd-root") + Path.DirectorySeparatorChar;
        var optical = AudioCdMetadataService.FindDiscRoots(
        [
            Root("media-1", opticalRoot, "CDRom"),
            File("file-1", "Track01.cda", Path.Combine(opticalRoot, "Track01.cda"), "media-1")
        ]);
        var tracksOnly = AudioCdMetadataService.FindDiscRoots(
        [
            Root("media-2", trackRoot, null),
            File("file-2", "Track01.cda", Path.Combine(trackRoot, "Track01.cda"), "media-2")
        ]);

        var disc = Assert.Single(optical);
        Assert.Equal(opticalRoot, disc.RootPath);
        Assert.Equal("media-1", disc.RootDocument!.Id);
        Assert.Equal(trackRoot.TrimEnd(Path.DirectorySeparatorChar), Assert.Single(tracksOnly).RootPath);
    }

    [Fact]
    public void UnixDeviceRoot_IsFoundWithoutTrackFiles()
    {
        var linux = AudioCdMetadataService.FindDiscRoots([Root("media-1", "/dev/sr0", "CDRom")]);
        var mac = AudioCdMetadataService.FindDiscRoots([Root("media-1", "/dev/rdisk4", "cdrom")]);

        Assert.Equal("/dev/sr0", Assert.Single(linux).RootPath);
        Assert.Equal("/dev/rdisk4", Assert.Single(mac).RootPath);
    }

    [Fact]
    public async Task IdentifiedDisc_StagesDiscAndTrackRecords()
    {
        var documents = DiscDocuments();
        var store = MediaCacheStore.Create();
        var service = new AudioCdMetadataService(new StubProvider(Capture()), store);
        var batchId = store.BeginBatch().BatchId;

        var staged = await service.CollectAsync(documents, documents, batchId);
        store.PublishBatch(batchId);

        Assert.Equal(3, staged);
        var discRecord = Assert.Single(store.GetRecordsForEntry("media-1"));
        Assert.Equal("skycd.audio-cd.disc", discRecord.SchemaId);
        Assert.Contains(discRecord.Metadata, value => value.Key == "Album" && value.Text == "Test Album");
        Assert.Single(discRecord.Artifacts);

        var firstTrack = Assert.Single(store.GetRecordsForEntry("file-1"));
        Assert.Equal("skycd.audio-cd.track", firstTrack.SchemaId);
        Assert.Contains(firstTrack.Metadata, value => value.Key == "Title" && value.Text == "Opening");
        Assert.Single(store.GetRecordsForEntry("file-2"));
        Assert.Empty(store.GetRecordsForEntry("file-3"));
    }

    [Fact]
    public async Task StoredDiscMetadata_StaysReadableThroughThePreviewResolver()
    {
        var documents = DiscDocuments();
        var store = MediaCacheStore.Create();
        var service = new AudioCdMetadataService(new StubProvider(Capture()), store);
        var batchId = store.BeginBatch().BatchId;
        await service.CollectAsync(documents, documents, batchId);
        store.PublishBatch(batchId);

        var snapshot = new MediaPreviewResolver(store).Resolve(documents[0]);

        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
        Assert.Contains(snapshot.Fields, field => field.Key == "Album" && field.Value as string == "Test Album");
    }

    [Fact]
    public async Task UnmatchedDisc_StagesNothing()
    {
        var documents = DiscDocuments();
        var store = MediaCacheStore.Create();
        var service = new AudioCdMetadataService(new StubProvider(null), store);
        var batchId = store.BeginBatch().BatchId;

        Assert.Equal(0, await service.CollectAsync(documents, documents, batchId));
        store.PublishBatch(batchId);
        Assert.Empty(store.GetRecordsForEntry("media-1"));
    }

    [Fact]
    public async Task FailingProvider_DoesNotBreakTheScan()
    {
        var documents = DiscDocuments();
        var store = MediaCacheStore.Create();
        var service = new AudioCdMetadataService(new ThrowingProvider(), store);

        Assert.Equal(0, await service.CollectAsync(documents, documents, store.BeginBatch().BatchId));
    }

    [Fact]
    public async Task ProviderWithoutAudioCdSupport_ReportsNothingToCollect()
    {
        var store = MediaCacheStore.Create();
        var service = new AudioCdMetadataService(new StubProvider(null, hasProviders: false), store);

        Assert.False(service.CanCollect);
        Assert.Equal(0, await service.CollectAsync(DiscDocuments(), DiscDocuments(),
            store.BeginBatch().BatchId));
    }

    private static IReadOnlyList<CatalogDocument> DiscDocuments()
    {
        // The disc root only has to look like an optical mount; nothing is read from disk.
        using var workspace = new TestWorkspace();
        var discRoot = workspace.GetPath("skycd-audio-cd-disc") + Path.DirectorySeparatorChar;
        return
        [
            Root("media-1", discRoot, "CDRom"),
            File("file-1", "Track01.cda", Path.Combine(discRoot, "Track01.cda"), "media-1"),
            File("file-2", "Track02.cda", Path.Combine(discRoot, "Track02.cda"), "media-1"),
            File("file-3", "Track03.cda", Path.Combine(discRoot, "Track03.cda"), "media-1")
        ];
    }

    private static CatalogDocument Root(string id, string rootDirectory, string? driveType)
    {
        var properties = new PropertiesCollection { ["RootDirectory"] = rootDirectory };
        if (driveType is not null)
        {
            properties["DriveType"] = driveType;
        }

        return new CatalogDocument
        {
            Id = id,
            Name = "Disc",
            Type = CatalogDocumentType.Media,
            Properties = properties
        };
    }

    private static CatalogDocument File(string id, string name, string fullName, string parentId) =>
        new()
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            Type = CatalogDocumentType.File,
            Properties = new PropertiesCollection { ["FullName"] = fullName }
        };

    private static AudioCdProviderCapture Capture()
    {
        var discSchema = new MediaMetadataSchema("skycd.audio-cd.disc", 1,
            [new MediaMetadataField("Album", MediaMetadataType.Text)]);
        var trackSchema = new MediaMetadataSchema("skycd.audio-cd.track", 1,
            [new MediaMetadataField("Title", MediaMetadataType.Text)]);
        var cover = new MediaPreviewArtifact("image/png", AudioCdFixtures.CoverPng(20));

        return new AudioCdProviderCapture("SkyCD.Plugin.AudioCd", "1.0.0", "audio-cd",
            new AudioCdCapture(
                discSchema,
                [new MediaMetadataValue("Album", MediaMetadataType.Text, Text: "Test Album")],
                trackSchema,
                [
                    new AudioCdTrackCapture(1,
                        [new MediaMetadataValue("Title", MediaMetadataType.Text, Text: "Opening")], cover),
                    new AudioCdTrackCapture(2,
                        [new MediaMetadataValue("Title", MediaMetadataType.Text, Text: "Second")], cover)
                ],
                cover));
    }

    private sealed class StubProvider : IAudioCdMetadataProvider
    {
        private readonly AudioCdProviderCapture? capture;

        public StubProvider(AudioCdProviderCapture? capture, bool hasProviders = true)
        {
            this.capture = capture;
            HasProviders = hasProviders;
        }

        public bool HasProviders { get; }

        public int Calls { get; private set; }

        public Task<AudioCdProviderCapture?> CaptureAsync(string rootPath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(capture);
        }
    }

    private sealed class ThrowingProvider : IAudioCdMetadataProvider
    {
        public bool HasProviders => true;

        public Task<AudioCdProviderCapture?> CaptureAsync(string rootPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provider failed");
    }
}
