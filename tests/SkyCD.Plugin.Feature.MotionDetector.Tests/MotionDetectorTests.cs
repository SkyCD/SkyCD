using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Configuration;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.SDK.Capabilities.Storage;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.MotionDetector.Tests;

public sealed class MotionDetectorTests
{
    [Fact]
    public void Plugin_IsACaptureSourceNotAToolsCommand()
    {
        var plugin = new MotionDetectorPlugin();

        Assert.IsAssignableFrom<IActivatablePluginCapability>(plugin);
        Assert.IsAssignableFrom<ICaptureSourcePluginCapability>(plugin);
        Assert.IsAssignableFrom<IOptionsPageCapability>(plugin);
        Assert.IsNotAssignableFrom<IMenuPluginCapability>(plugin);
        Assert.Equal("motion-detector", plugin.ProviderId);
    }

    [Fact]
    public void Plugin_OffersEverySourceKind()
    {
        var plugin = new MotionDetectorPlugin();

        var kinds = plugin.GetSourceKinds().Select(kind => kind.Id).ToArray();

        Assert.Equal(
            [CaptureSourceKinds.File, CaptureSourceKinds.Device, CaptureSourceKinds.JpegUrl,
                CaptureSourceKinds.MjpegUrl, CaptureSourceKinds.MmsStream],
            kinds);
        Assert.True(plugin.GetSourceKinds().Single(kind => kind.Id == CaptureSourceKinds.File).UseFilePicker);
        Assert.True(plugin.GetSourceKinds().Single(kind => kind.Id == CaptureSourceKinds.Device).UseDevicePicker);
    }

    [Fact]
    public void DetectorCatalog_ListsTheScientificDetectors()
    {
        var descriptors = DetectorCatalog.Describe();

        Assert.Equal(
            [
                DetectorCatalog.FrameDifference,
                DetectorCatalog.RunningAverage,
                DetectorCatalog.Mog2,
                DetectorCatalog.Knn,
                DetectorCatalog.Farneback,
                DetectorCatalog.EdgeChange,
                DetectorCatalog.PerceptualDifference
            ],
            descriptors.Select(descriptor => descriptor.Id));
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Assert.NotEmpty(descriptor.Parameters);
        });

        var mog2 = descriptors.Single(descriptor => descriptor.Id == DetectorCatalog.Mog2);
        Assert.Equal(
            ["history", "varianceThreshold", "learningRate", "minAreaPercent"],
            mog2.Parameters.Select(parameter => parameter.Key));
        Assert.All(mog2.Parameters, parameter =>
            Assert.True(parameter.Minimum <= parameter.DefaultValue &&
                        parameter.DefaultValue <= parameter.Maximum));
    }

    [Fact]
    public void Translations_NameEveryDetectorInBothLanguages()
    {
        Assert.Equal(["en", "lt"], MotionDetectorTranslations.All.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach (var (culture, yaml) in MotionDetectorTranslations.All)
        {
            foreach (var spec in DetectorCatalog.Specs)
            {
                Assert.Contains($"ui.Detector.{spec.Id}.Name:", yaml, StringComparison.Ordinal);
                Assert.Contains($"ui.Detector.{spec.Id}.Description:", yaml, StringComparison.Ordinal);
            }

            Assert.Contains("ui.OptionsPage:", yaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DetectorCatalog_NormalizesParametersWithinTheirRanges()
    {
        var normalized = DetectorCatalog.Normalize(DetectorCatalog.FrameDifference,
            new Dictionary<string, double> { ["threshold"] = 999, ["minAreaPercent"] = -5 });

        Assert.Equal(255, normalized["threshold"]);
        Assert.Equal(0, normalized["minAreaPercent"]);
        Assert.Empty(DetectorCatalog.Normalize("unknown", null));
    }

    [Fact]
    public void StateStore_RoundTripsSourcesAndSettings()
    {
        var storage = new FakeStorage();
        var source = new CaptureSourceConfiguration(
            "source-1",
            CaptureSourceKinds.JpegUrl,
            "http://camera.local/frame.jpg",
            @"D:\captures",
            "Motion",
            "Front door");

        new PluginStateStore(storage).SaveSource(source);
        new PluginStateStore(storage).SaveSettings(new CaptureDetectorSettings(
            DetectorCatalog.Mog2, new Dictionary<string, double> { ["learningRate"] = 0.5 }));

        var reloaded = new PluginStateStore(storage);

        Assert.Equal(source, Assert.Single(reloaded.LoadSources()));
        var settings = reloaded.LoadSettings();
        Assert.Equal(DetectorCatalog.Mog2, settings.DetectorId);
        Assert.Equal(0.5, settings.Parameters["learningRate"]);
        Assert.Equal(500, settings.Parameters["history"]);
    }

    [Fact]
    public void StateStore_DropsUnknownDetectorAndKeepsDefaults()
    {
        var storage = new FakeStorage();
        new PluginStateStore(storage).SaveSettings(new CaptureDetectorSettings("unknown-detector", new Dictionary<string, double>()));

        var loaded = new PluginStateStore(storage).LoadSettings();

        Assert.Null(loaded.DetectorId);
        Assert.Empty(loaded.Parameters);
        Assert.Equal(DetectorCatalog.FrameDifference, PluginStateStore.DefaultSettings().DetectorId);
    }

    [Fact]
    public void StateStore_DeletesRemovedSource()
    {
        var storage = new FakeStorage();
        var store = new PluginStateStore(storage);
        store.SaveSource(Source("source-1"));
        store.SaveSource(Source("source-2"));

        store.DeleteSource("source-1");

        Assert.Equal("source-2", Assert.Single(new PluginStateStore(storage).LoadSources()).SourceId);
    }

    [Fact]
    public async Task AddSourceAsync_PersistsAndReportsWithoutACaptureLoopWhenInactive()
    {
        using var workspace = new TestWorkspace();
        var storage = new FakeStorage();
        var host = new FakeHost { IsCatalogOpen = true };
        var plugin = new MotionDetectorPlugin();
        plugin.InitializeStorage(storage);
        plugin.InitializeCaptureHost(host);

        var destination = workspace.Root;

        var added = await plugin.AddSourceAsync(new CaptureSourceDraft(
            CaptureSourceKinds.JpegUrl, "http://camera.local/frame.jpg", destination, string.Empty, string.Empty));

        Assert.Equal(CaptureSourceKinds.JpegUrl, added.KindId);
        Assert.Equal("Motion", added.CatalogFolder);
        Assert.Equal(CaptureSourceKinds.JpegUrl, added.Label);
        Assert.Equal(added, Assert.Single(plugin.GetSources()));
        Assert.Equal(added, Assert.Single(new PluginStateStore(storage).LoadSources()));
        Assert.Contains(host.Messages, message => message.Contains(MotionDetectorKeys.SourceAdded, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Probe_RequiresAnOpenCatalogAndAQualifiedDestination()
    {
        using var workspace = new TestWorkspace();
        var plugin = new MotionDetectorPlugin();
        plugin.InitializeStorage(new FakeStorage());
        plugin.InitializeCaptureHost(new FakeHost { IsCatalogOpen = false });

        var configuration = Source("source-1");

        var unavailable = await plugin.ProbeAsync(configuration);
        Assert.False(unavailable.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Message));

        plugin.InitializeCaptureHost(new FakeHost { IsCatalogOpen = true });
        var withoutDestination = await plugin.ProbeAsync(configuration with { DestinationFolder = "relative" });
        Assert.False(withoutDestination.IsAvailable);

        var destination = workspace.Root;

        var valid = await plugin.ProbeAsync(configuration with { DestinationFolder = destination });
        Assert.True(valid.IsAvailable);
    }

    [Fact]
    public void BundledOpenCvRuntime_LoadsOnTheCurrentPlatform()
    {
        Assert.True(NativeOpenCv.EnsureLoaded(),
            $"the {NativeOpenCv.LibraryFileName} bundled for {RuntimeInformation.RuntimeIdentifier} did not load");

        using var frame = new Mat(2, 2, MatType.CV_8UC1);
        Assert.Equal(2, frame.Rows);
    }

    private static CaptureSourceConfiguration Source(string id) =>
        new(id, CaptureSourceKinds.JpegUrl, "http://camera.local/frame.jpg", @"D:\captures", "Motion", id);

    private sealed class FakeHost : ICaptureSourceHostApi
    {
        public bool IsCatalogOpen { get; set; }

        public List<string> Messages { get; } = [];

        public Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorage : IPluginStorageApi
    {
        private readonly Dictionary<string, FakeCollection> collections = new(StringComparer.Ordinal);

        public IPluginCollection OpenCollection(string name)
        {
            if (!collections.TryGetValue(name, out var collection))
            {
                collection = new FakeCollection(name);
                collections[name] = collection;
            }

            return collection;
        }

        private sealed class FakeCollection(string name) : IPluginCollection
        {
            private readonly Dictionary<string, PluginDocumentRecord> records = new(StringComparer.Ordinal);

            public string Name { get; } = name;

            public PluginDocumentRecord? Get(string id) =>
                records.TryGetValue(id, out var record) ? record : null;

            public IReadOnlyList<PluginDocumentRecord> GetAll() => records.Values.ToArray();

            public void Upsert(string id, IReadOnlyDictionary<string, object?> fields) =>
                records[id] = new PluginDocumentRecord(id, fields);

            public void Delete(string id) => records.Remove(id);
        }
    }
}
