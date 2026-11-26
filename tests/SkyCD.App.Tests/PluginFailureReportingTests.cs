using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1202: plugin failures name the plugin and collapse per fingerprint. A preview that
/// throws is still recorded so a failed refresh can keep the previous capture. An unavailable
/// decoder is a warning and does not run.
/// </summary>
public sealed class PluginFailureReportingTests
{
    [Fact]
    public void Create_NamesPluginAndTypesWithoutMessages()
    {
        var first = PluginFailure.Create(typeof(PluginFailureReportingTests), "media",
            new InvalidOperationException("secret detail"));
        var second = PluginFailure.Create(typeof(PluginFailureReportingTests), "media",
            new InvalidOperationException("other detail"));

        Assert.Equal("SkyCD.App.Tests", first.PluginId);
        Assert.Equal("InvalidOperationException", first.ExceptionType);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Null(first.Reason);
        Assert.Null(first.ItemName);
        Assert.False(first.IsWarning);
    }

    [Fact]
    public void Publish_CarriesRoutingWithoutChangingFingerprint()
    {
        var received = new List<PluginFailure>();
        void Handler(PluginFailure failure) => received.Add(failure);
        PluginFailure.Reported += Handler;
        try
        {
            var exception = new FileNotFoundException("missing");
            PluginFailure.Publish(typeof(PluginFailureReportingTests), "media", exception,
                pluginId: "SkyCD.Plugin.Metadata.Video",
                reason: "poster decoder unavailable",
                itemName: "a.mp4");

            var failure = Assert.Single(received);
            Assert.Equal("SkyCD.Plugin.Metadata.Video", failure.PluginId);
            Assert.Equal("poster decoder unavailable", failure.Reason);
            Assert.Equal("a.mp4", failure.ItemName);
            Assert.Equal(PluginFailure.Create(typeof(PluginFailureReportingTests), "media", exception).Fingerprint,
                failure.Fingerprint);
        }
        finally
        {
            PluginFailure.Reported -= Handler;
        }
    }

    [Fact]
    public void ReportPluginFailure_NamesPluginAndCollapsesRepeats()
    {
        var vm = new AddingProgressDialogViewModel();
        var first = PluginFailure.Create(typeof(PluginFailureReportingTests), "media",
                new FileNotFoundException("missing"))
            with
        {
            PluginId = "SkyCD.Plugin.Metadata.Video",
            PluginName = "Video Metadata and Poster Frames",
            Reason = "poster decoder unavailable",
            ItemName = "a.mp4",
        };
        var second = first with { ItemName = "b.mp4" };

        vm.ReportPluginFailure(first);
        vm.ReportPluginFailure(second);

        var entry = Assert.Single(vm.LogEntries);
        var text = vm.GetFullLogText();
        Assert.Contains("SkyCD.Plugin.Metadata.Video", text, StringComparison.Ordinal);
        Assert.Contains("Video Metadata and Poster Frames", text, StringComparison.Ordinal);
        Assert.Contains("FileNotFoundException", text, StringComparison.Ordinal);
        Assert.Contains("×2", entry.DisplayText, StringComparison.Ordinal);
        Assert.Contains("a.mp4", entry.DisplayText, StringComparison.Ordinal);
        Assert.Contains("b.mp4", entry.DisplayText, StringComparison.Ordinal);
        Assert.Contains(first.Fingerprint, text, StringComparison.Ordinal);
        Assert.Equal(1, vm.ErrorCount);
    }

    [Fact]
    public void ReportPluginFailure_WarningRendersWarningLevel()
    {
        var vm = new AddingProgressDialogViewModel();
        var failure = PluginFailure.Create(typeof(PluginFailureReportingTests), "media",
                new InvalidOperationException("unavailable"))
            with
        {
            PluginId = "SkyCD.Plugin.Metadata.Video",
            PluginName = "Video Metadata and Poster Frames",
            Reason = "poster decoder unavailable",
            ItemName = "a.mp4",
            IsWarning = true,
        };

        vm.ReportPluginFailure(failure);
        vm.ReportPluginFailure(failure with { ItemName = "b.mp4" });

        var entry = Assert.Single(vm.LogEntries);
        Assert.Equal(OperationLogLevel.Warning, entry.Level);
        Assert.Equal(1, vm.WarningCount);
        Assert.Equal(0, vm.ErrorCount);
        Assert.Contains("SkyCD.Plugin.Metadata.Video", entry.DisplayText, StringComparison.Ordinal);
        Assert.Contains("poster decoder unavailable", entry.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowingPreview_RecordsTheFailureAndNamesThePlugin()
    {
        var received = new List<PluginFailure>();
        void Handler(PluginFailure failure) => received.Add(failure);
        PluginFailure.Reported += Handler;
        try
        {
            var manager = new MediaExtensionManager(
                [new MediaProvider("test.preview", "1.0", new ThrowingPreviewCapability())]);

            var results = await manager.ProcessAsync(
                new MediaItem(".mp4", "video/mp4", 10),
                _ => Task.FromResult<Stream>(new MemoryStream(new byte[10])));

            var failed = Assert.Single(results, static result => result.Failed);
            Assert.Equal("test.preview", failed.PluginId);
            var failure = Assert.Single(received, f => f.PluginId == "test.preview");
            Assert.Equal("InvalidOperationException", failure.ExceptionType);
            Assert.Contains("preview failure", failure.Reason ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            PluginFailure.Reported -= Handler;
        }
    }

    [Fact]
    public async Task ThrowingMetadata_StillFailsEntry()
    {
        var manager = new MediaExtensionManager(
            [new MediaProvider("test.metadata", "1.0", new ThrowingMetadataCapability())]);

        var results = await manager.ProcessAsync(
            new MediaItem(".mp3", "audio/mpeg", 10),
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[10])));

        var failed = Assert.Single(results, static result => result.Failed);
        Assert.Equal("test.metadata", failed.PluginId);
    }

    [Fact]
    public async Task UnavailablePreview_SkippedWithWarningAndStoredEntry()
    {
        var received = new List<PluginFailure>();
        void Handler(PluginFailure failure) => received.Add(failure);
        PluginFailure.Reported += Handler;
        try
        {
            var manager = new MediaExtensionManager(
                [new MediaProvider("test.poster", "1.0", new UnavailablePreviewCapability())]);

            var results = await manager.ProcessAsync(
                new MediaItem(".mp4", "video/mp4", 10),
                _ => Task.FromResult<Stream>(new MemoryStream(new byte[10])),
                sourcePath: Path.Combine("Videos", "clip.mp4"));

            Assert.Empty(results);
            var failure = Assert.Single(received);
            Assert.True(failure.IsWarning);
            Assert.Equal("test.poster", failure.PluginId);
            Assert.Equal("poster decoder unavailable", failure.Reason);
            Assert.Equal("clip.mp4", failure.ItemName);
        }
        finally
        {
            PluginFailure.Reported -= Handler;
        }
    }

    private sealed class ThrowingPreviewCapability : IMediaPreviewCapability
    {
        public MediaDescriptor Descriptor => new([".mp4"], [], new MediaMetadataSchema("fake.preview", 1, []));

        public Task<MediaPreviewResult> GeneratePreviewAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("preview failure");
    }

    private sealed class ThrowingMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new(
            [".mp3"],
            ["audio/mpeg"],
            new MediaMetadataSchema("fake.metadata", 1, []));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("metadata failure");
    }

    private sealed class UnavailablePreviewCapability : IMediaPreviewCapability, IMediaPreviewAvailability
    {
        public MediaDescriptor Descriptor => new([".mp4"], [], new MediaMetadataSchema("fake.poster", 1, []));

        public string? PreviewUnavailableReason => "poster decoder unavailable";

        public Task<MediaPreviewResult> GeneratePreviewAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("must be skipped before running");
    }
}
