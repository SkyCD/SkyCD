using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Volume.Models;
using SkyCD.Plugin.Metadata.Volume.Sources;

namespace SkyCD.Plugin.Metadata.Volume.Tests;

public sealed class VolumeSerialPluginTests
{
    private static readonly MediaTarget Volume = new(MediaTargetKind.Volume, "E:\\", "E:\\", "MY_DISC", "CDFS");

    private sealed class FakeVolumeSerialApi(bool isSupported, bool succeeds, uint serial) : IVolumeSerialApi
    {
        public bool IsSupported => isSupported;
        public string? RequestedRootPath { get; private set; }

        public bool TryGetVolumeSerial(string rootPath, out uint volumeSerial)
        {
            RequestedRootPath = rootPath;
            volumeSerial = succeeds ? serial : 0;
            return succeeds;
        }
    }

    private static async Task<MediaTargetCapture?> CaptureAsync(VolumeSerialPlugin plugin,
        CancellationToken cancellationToken = default) =>
        await plugin.CaptureAsync(Volume, progress: null, cancellationToken);

    [Fact]
    public async Task Capture_KnownSerial_StoresLosslessDisplayForm()
    {
        var api = new FakeVolumeSerialApi(true, true, 0x1234ABCDu);
        var plugin = new VolumeSerialPlugin(api);

        var capture = await CaptureAsync(plugin);
        var value = Assert.Single(Assert.IsType<MediaTargetCapture>(capture).Values);

        Assert.Equal(VolumeSerialSchema.VolumeSerialNumber, value.Key);
        Assert.Equal(MediaMetadataType.Text, value.Type);
        Assert.Equal("1234-ABCD", value.Text);
        Assert.Equal("E:\\", api.RequestedRootPath);
    }

    [Fact]
    public async Task Capture_ZeroSerial_ReportsGenuineZeroInsteadOfFailure()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 0u));

        var capture = Assert.IsType<MediaTargetCapture>(await CaptureAsync(plugin));

        Assert.Equal("0000-0000", Assert.Single(capture.Values).Text);
    }

    [Fact]
    public async Task Capture_MaxSerial_IsLossless()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, uint.MaxValue));

        var capture = Assert.IsType<MediaTargetCapture>(await CaptureAsync(plugin));
        var value = Assert.Single(capture.Values);

        Assert.Equal("FFFF-FFFF", value.Text);
        Assert.True(VolumeSerialSchema.TryParse(value.Text, out var parsed));
        Assert.Equal(uint.MaxValue, parsed);
    }

    [Fact]
    public async Task Capture_ApiFailure_ReturnsNoValueInsteadOfPlaceholder()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, false, 0u));

        Assert.Null(await CaptureAsync(plugin));
    }

    [Fact]
    public async Task Capture_UnsupportedPlatform_ReturnsNoValue()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(false, true, 0x1234ABCDu));

        Assert.Null(await CaptureAsync(plugin));
    }

    [Fact]
    public void Supports_OnlyVolumes()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 1u));

        Assert.True(plugin.Supports(Volume));
        Assert.False(plugin.Supports(Volume with { Kind = MediaTargetKind.OpticalDisc }));
    }

    [Fact]
    public async Task Capture_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 1u));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            plugin.CaptureAsync(Volume, progress: null, cancellation.Token));
    }

    [Fact]
    public async Task Capture_NullTarget_Throws()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 1u));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            plugin.CaptureAsync(null!, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task Capture_PublishesStableSchema()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 0x1234ABCDu));

        var capture = Assert.IsType<MediaTargetCapture>(await CaptureAsync(plugin));

        Assert.Equal(VolumeSerialSchema.MetadataSchemaId, capture.Schema.Id);
        Assert.Equal(1, capture.Schema.Version);
        var field = Assert.Single(capture.Schema.Fields);
        Assert.Equal(VolumeSerialSchema.VolumeSerialNumber, field.Key);
        Assert.Equal(MediaMetadataType.Text, field.Type);
        Assert.Null(plugin.Descriptor.PrimaryExtension);
    }

    [Theory]
    [InlineData(0u, "0000-0000")]
    [InlineData(0x00010002u, "0001-0002")]
    [InlineData(0x1234ABCDu, "1234-ABCD")]
    [InlineData(uint.MaxValue, "FFFF-FFFF")]
    public void Format_TryParse_RoundTripsLosslessly(uint serial, string expected)
    {
        Assert.Equal(expected, VolumeSerialSchema.Format(serial));
        Assert.True(VolumeSerialSchema.TryParse(expected, out var parsed));
        Assert.Equal(serial, parsed);
    }
}
