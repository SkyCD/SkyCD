using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Capabilities.Volume;
using SkyCD.Plugin.Host.Volume;
using Xunit;
using SkyCD.Plugin.Metadata.Volume.Models;

namespace SkyCD.Plugin.Metadata.Volume.Tests;

/// <summary>
/// Host-level <see cref="VolumeExtensionManager"/> behavior and schema edge cases not covered by the
/// per-plugin tests: provider ordering, first-wins key dedupe, fault isolation, and input rejection.
/// </summary>
public sealed class VolumeExtensionManagerTests
{
    private static readonly VolumeMediaContext Context = new(@"C:\", "C:\\");

    private static MediaMetadataValue MakeValue(string key, string text) =>
        new(key, MediaMetadataType.Text, Text: text);

    private sealed class FakeVolumeCapability : IVolumeMetadataCapability
    {
        public IReadOnlyList<MediaMetadataValue> Values { get; init; } = [];
        public bool Throw { get; init; }

        public VolumeDescriptor Descriptor => new(new MediaMetadataSchema("fake", 1, []));

        public VolumeExtractionResult Extract(VolumeMediaContext volume, CancellationToken cancellationToken)
        {
            if (Throw) throw new InvalidOperationException("Fake plugin failure");
            return new VolumeExtractionResult(Values);
        }
    }

    [Fact]
    public void NoProviders_ReturnsEmpty()
    {
        var manager = new VolumeExtensionManager([]);

        Assert.Empty(manager.CaptureVolumeMetadata(Context, CancellationToken.None));
        Assert.False(manager.HasVolumeProviders);
    }

    [Fact]
    public void SingleProvider_ReturnsItsValues()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability { Values = [MakeValue("k1", "v1")] })]);

        var values = manager.CaptureVolumeMetadata(Context, CancellationToken.None);

        Assert.Single(values, v => v.Key == "k1" && v.Text == "v1");
        Assert.True(manager.HasVolumeProviders);
    }

    [Fact]
    public void MultipleProviders_CollectsAllValues()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability { Values = [MakeValue("k1", "v1")] }),
            new VolumeProvider("b", "1", new FakeVolumeCapability { Values = [MakeValue("k2", "v2")] }),
        ]);

        var values = manager.CaptureVolumeMetadata(Context, CancellationToken.None);

        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void DuplicateKeys_FirstProviderWins()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("aaa", "1", new FakeVolumeCapability { Values = [MakeValue("shared", "first")] }),
            new VolumeProvider("zzz", "1", new FakeVolumeCapability { Values = [MakeValue("shared", "second")] }),
        ]);

        var values = manager.CaptureVolumeMetadata(Context, CancellationToken.None);

        Assert.Equal("first", Assert.Single(values).Text);
    }

    [Fact]
    public void ProvidersAreOrderedByPluginIdOrdinal()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("zzz", "1", new FakeVolumeCapability { Values = [MakeValue("k", "z")] }),
            new VolumeProvider("aaa", "1", new FakeVolumeCapability { Values = [MakeValue("k2", "a")] }),
        ]);

        var values = manager.CaptureVolumeMetadata(Context, CancellationToken.None);

        Assert.Equal("k2", values[0].Key);
        Assert.Equal("k", values[1].Key);
    }

    [Fact]
    public void FaultyProvider_DoesNotBreakRemainingProviders()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("bad", "1", new FakeVolumeCapability { Throw = true }),
            new VolumeProvider("good", "1", new FakeVolumeCapability { Values = [MakeValue("ok", "yes")] }),
        ]);

        var values = manager.CaptureVolumeMetadata(Context, CancellationToken.None);

        Assert.Single(values, v => v.Key == "ok");
    }

    [Fact]
    public void CancelledToken_ThrowsBeforeAnyCapture()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability())]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => manager.CaptureVolumeMetadata(Context, cts.Token));
    }

    [Fact]
    public void NullContext_Throws()
    {
        var manager = new VolumeExtensionManager([]);

        Assert.Throws<ArgumentNullException>(() => manager.CaptureVolumeMetadata(null!, CancellationToken.None));
    }

    [Fact]
    public void EmptyResult_IsNotAdded()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability { Values = [] })]);

        Assert.Empty(manager.CaptureVolumeMetadata(Context, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("1234-5678-9ABC")]
    [InlineData("XYZW-1234")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234_5678")]
    [InlineData("1234 5678")]
    public void TryParse_RejectsInvalidInput(string? text)
    {
        Assert.False(VolumeSerialSchema.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_AcceptsLowerCaseHex()
    {
        Assert.True(VolumeSerialSchema.TryParse("abcd-1234", out var serial));
        Assert.Equal(0xABCD1234u, serial);
    }
}
