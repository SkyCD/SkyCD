using System;
using System.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using Xunit;
using SkyCD.Plugin.Metadata.Volume.Models;
using SkyCD.Plugin.Metadata.Volume.Sources;

namespace SkyCD.Plugin.Metadata.Volume.Tests;

public sealed class VolumeSerialPluginTests
{
    private static readonly VolumeMediaContext Volume = new("E:\\", "E:\\", "MY_DISC", "CDFS");

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

    [Fact]
    public void Extract_KnownSerial_StoresLosslessDisplayForm()
    {
        var api = new FakeVolumeSerialApi(true, true, 0x1234ABCDu);
        var plugin = new VolumeSerialPlugin(api);

        var value = Assert.Single(plugin.Extract(Volume, CancellationToken.None).Values);

        Assert.Equal(VolumeSerialSchema.VolumeSerialNumber, value.Key);
        Assert.Equal(MediaMetadataType.Text, value.Type);
        Assert.Equal("1234-ABCD", value.Text);
        Assert.Equal("E:\\", api.RequestedRootPath);
    }

    [Fact]
    public void Extract_ZeroSerial_ReportsGenuineZeroInsteadOfFailure()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 0u));

        var value = Assert.Single(plugin.Extract(Volume, CancellationToken.None).Values);

        Assert.Equal("0000-0000", value.Text);
    }

    [Fact]
    public void Extract_MaxSerial_IsLossless()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, uint.MaxValue));

        var value = Assert.Single(plugin.Extract(Volume, CancellationToken.None).Values);

        Assert.Equal("FFFF-FFFF", value.Text);
        Assert.True(VolumeSerialSchema.TryParse(value.Text, out var parsed));
        Assert.Equal(uint.MaxValue, parsed);
    }

    [Fact]
    public void Extract_ApiFailure_ReturnsNoValueInsteadOfPlaceholder()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, false, 0u));

        var result = plugin.Extract(Volume, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void Extract_UnsupportedPlatform_ReturnsNoValue()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(false, true, 0x1234ABCDu));

        var result = plugin.Extract(Volume, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void Extract_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 1u));

        Assert.ThrowsAny<OperationCanceledException>(() => plugin.Extract(Volume, cancellation.Token));
    }

    [Fact]
    public void Extract_NullVolume_Throws()
    {
        var plugin = new VolumeSerialPlugin(new FakeVolumeSerialApi(true, true, 1u));

        Assert.Throws<ArgumentNullException>(() => plugin.Extract(null!, CancellationToken.None));
    }

    [Fact]
    public void Descriptor_PublishesStableSchema()
    {
        var descriptor = new VolumeSerialPlugin(new FakeVolumeSerialApi(false, false, 0u)).Descriptor;

        Assert.Equal(VolumeSerialSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        var field = Assert.Single(descriptor.Schema.Fields);
        Assert.Equal(VolumeSerialSchema.VolumeSerialNumber, field.Key);
        Assert.Equal(MediaMetadataType.Text, field.Type);
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
