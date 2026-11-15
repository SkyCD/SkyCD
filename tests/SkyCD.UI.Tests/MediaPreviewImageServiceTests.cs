using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.MediaCache.Exceptions;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Preview;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Covers the decode half of the offline preview surface on a real Avalonia platform: a stored artifact
/// becomes an image without any plugin present, an undecodable artifact degrades to the failed state instead
/// of breaking navigation, and the browser thumbnail source answers from the cache alone.
/// </summary>
public sealed class MediaPreviewImageServiceTests
{
    private const string EntryId = "file-cover-1";

    [AvaloniaFact]
    public async Task StoredArtifact_DecodesToAnImage_WithoutAnyPluginRegistered()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, RepositoryPng());

        using var images = new MediaPreviewImageService(new MediaPreviewResolver(store));
        var snapshot = new MediaPreviewResolver(store).Resolve(entry);

        var decoded = await images.GetImageAsync(snapshot);

        Assert.NotNull(decoded);
        Assert.True(decoded!.Size.Width > 0);
        Assert.Equal(1, images.CachedImageCount);

        // A second request is served from the bounded decode cache, not decoded again.
        var cached = await images.GetImageAsync(snapshot);
        Assert.Same(decoded, cached);
    }

    [AvaloniaFact]
    public async Task UndecodableArtifact_SurfacesAsADecodeFailure()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, [1, 2, 3, 4, 5]);

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver, decoder: new FaultingDecoder());
        var snapshot = resolver.Resolve(entry);

        await Assert.ThrowsAsync<MediaPreviewDecodeException>(() => images.GetImageAsync(snapshot));
        Assert.Equal(0, images.CachedImageCount);
    }

    [AvaloniaFact]
    public async Task PreviewViewModel_ReportsFailedState_WhenTheArtifactCannotBeDecoded()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, [1, 2, 3, 4, 5]);

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver, decoder: new FaultingDecoder());
        using var preview = new EntryPreviewViewModel(resolver, images);

        await preview.ShowAsync(entry);

        Assert.Equal(MediaPreviewState.Failed, preview.State);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.StateFailed), preview.StatusText);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.PreviewFailed), preview.StateMessage);
        Assert.Null(preview.PreviewImage);
    }

    [AvaloniaFact]
    public async Task DecoderReturningNoImage_LeavesThePaneOnItsExplanationMessage()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, RepositoryPng());

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver, decoder: new EmptyDecoder());
        using var preview = new EntryPreviewViewModel(resolver, images);

        await preview.ShowAsync(entry);

        Assert.Equal(MediaPreviewState.Ready, preview.State);
        Assert.Null(preview.PreviewImage);
        Assert.Equal(LocalizationService.Current.Get(PreviewKeys.NoPreviewStored), preview.StateMessage);
    }

    [AvaloniaFact]
    public async Task PreviewViewModel_ShowsTheDecodedImageAndItsAccessibleName()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, RepositoryPng());

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver);
        using var preview = new EntryPreviewViewModel(resolver, images);

        await preview.ShowAsync(entry);

        Assert.Equal(MediaPreviewState.Ready, preview.State);
        Assert.NotNull(preview.PreviewImage);
        Assert.Null(preview.StateMessage);
        Assert.Equal(
            LocalizationService.Current.Format(PreviewKeys.PreviewImageAutomation, entry.Name),
            preview.PreviewAutomationName);
    }

    [AvaloniaFact]
    public async Task BrowserThumbnails_ComeFromTheCacheAndStayHiddenWithoutOne()
    {
        using var store = MediaCacheStore.Create();
        var withPreview = ImageEntry();
        var withoutPreview = new CatalogDocument
        {
            Id = "file-plain-1",
            Name = "archive.iso",
            Type = CatalogDocumentType.File,
            Size = 1024
        };
        SaveArtifact(store, withPreview.Id, RepositoryPng());

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver);
        var provider = new MediaCacheThumbnailProvider(resolver, images);

        var thumbnail = await provider.GetThumbnailAsync(withPreview, CancellationToken.None);
        Assert.NotNull(thumbnail);

        var missing = await provider.GetThumbnailAsync(withoutPreview, CancellationToken.None);
        Assert.Null(missing);

        var notAnEntry = await provider.GetThumbnailAsync(new object(), CancellationToken.None);
        Assert.Null(notAnEntry);
    }

    [AvaloniaFact]
    public async Task ThumbnailProvider_AnswersWithoutTouchingTheOriginalSource()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, RepositoryPng());

        // The catalog points at a path that does not exist anywhere on this machine.
        var disconnected = new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            Type = entry.Type,
            Size = entry.Size,
            Properties = new PropertiesCollection { ["FullName"] = Path.Combine(Path.GetTempPath(), "gone", entry.Name) }
        };
        Assert.False(File.Exists(disconnected.Properties["FullName"]!.ToString()));

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver);
        var provider = new MediaCacheThumbnailProvider(resolver, images);

        Assert.NotNull(await provider.GetThumbnailAsync(disconnected, CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task CancelledThumbnailRequest_DoesNotProduceAnImage()
    {
        using var store = MediaCacheStore.Create();
        var entry = ImageEntry();
        SaveArtifact(store, entry.Id, RepositoryPng());

        var resolver = new MediaPreviewResolver(store);
        using var images = new MediaPreviewImageService(resolver);
        var provider = new MediaCacheThumbnailProvider(resolver, images);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetThumbnailAsync(entry, cancellation.Token));
    }

    private static CatalogDocument ImageEntry() => new()
    {
        Id = EntryId,
        Name = "cover.jpg",
        Type = CatalogDocumentType.File,
        Size = 1024
    };

    private static void SaveArtifact(IMediaCacheStore store, string entryId, byte[] bytes)
    {
        var fingerprint = $"fingerprint-{entryId}";
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, "plugin.image", "1.0.0", "Preview", "fake.image", 1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = "plugin.image",
            PluginVersion = "1.0.0",
            Capability = "Preview",
            SchemaId = "fake.image",
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
            SourceLength = bytes.Length,
            Artifacts =
            [
                new MediaArtifactReference
                {
                    Key = "preview:0",
                    ContentType = "image/png",
                    Length = bytes.Length,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
                }
            ]
        }.WithRecalculatedSizes();

        store.Save(new MediaCachePayload(record, new Dictionary<string, byte[]> { ["preview:0"] = bytes }));
    }

    /// <summary>A real PNG from the repository assets, so the decode path is exercised end to end.</summary>
    private static byte[] RepositoryPng() =>
        File.ReadAllBytes(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Assets", "new.png"));

    private sealed class FaultingDecoder : IPreviewImageDecoder
    {
        public Task<Avalonia.Media.IImage?> DecodeAsync(
            byte[] data,
            string contentType,
            long declaredLength,
            CancellationToken cancellationToken) =>
            throw new MediaPreviewDecodeException(contentType, declaredLength, new InvalidDataException("no pixels"));
    }

    private sealed class EmptyDecoder : IPreviewImageDecoder
    {
        public Task<Avalonia.Media.IImage?> DecodeAsync(
            byte[] data,
            string contentType,
            long declaredLength,
            CancellationToken cancellationToken) =>
            Task.FromResult<Avalonia.Media.IImage?>(null);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
