using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// Issue #1036: every plugin library ships its own icon as an embedded ICO, so the host never has to guess
/// an icon from the assembly name or the capabilities a plugin happens to implement.
/// </summary>
public sealed class PluginIconResolutionTests
{
    [Fact]
    public void BuildFromAssembly_ReadsTheIconEmbeddedInTheLibrary()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());

        Assert.NotNull(plugin.Icon);
        Assert.True(plugin.Icon.Length > 8);
        Assert.Equal(0x89, plugin.Icon[0]);
        Assert.Equal(0x50, plugin.Icon[1]);
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(plugin.Icon.AsSpan(16, 4)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(plugin.Icon.AsSpan(20, 4)));
    }

    [Fact]
    public void Resolve_ReturnsNullWhenTheAssemblyShipsNoIcon()
    {
        Assert.Null(PluginIconResolver.Resolve(typeof(string).Assembly));
    }

    [Fact]
    public void ToDocument_PersistsTheIconForStaleDescriptors()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());

        var document = plugin.ToDocument();

        Assert.Equal(Convert.ToBase64String(plugin.Icon!), document.IconPng);
    }

    [Fact]
    public void Read_RejectsPayloadsThatAreNotIco()
    {
        using var stream = new MemoryStream("not a png icon"u8.ToArray());

        Assert.Null(PluginIconResolver.Read(stream));
    }

    [Fact]
    public void Read_RejectsOversizedPayloads()
    {
        using var stream = new MemoryStream(new byte[PluginIconResolver.MaxIconBytes + 1]);

        Assert.Null(PluginIconResolver.Read(stream));
    }

    [Fact]
    public void ReadFrames_ReturnsTheEmbeddedFrameLadder()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());

        Assert.NotEmpty(plugin.IconFrames);
        Assert.All(plugin.IconFrames, frame => Assert.True(frame.Size > 0));
        Assert.All(plugin.IconFrames, frame => AssertPngSize(frame.Png, frame.Size));
        Assert.Contains(plugin.IconFrames, frame => frame.Size == 24);
        Assert.Contains(plugin.IconFrames, frame => frame.Size == 48);
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(48, 48)]
    [InlineData(1000, 256)]
    [InlineData(1, 16)]
    public void GetIconNearest_ReturnsTheClosestFrame(int targetSize, int expectedSize)
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());

        var nearest = plugin.GetIconNearest(targetSize);

        Assert.NotNull(nearest);
        AssertPngSize(nearest, expectedSize);
    }

    [Fact]
    public void SelectNearest_PrefersTheLargerFrameOnATie()
    {
        var frames = new[]
        {
            new PluginIconFrame(16, [0x89]),
            new PluginIconFrame(48, [0x50])
        };

        Assert.Equal(new byte[] { 0x50 }, PluginIconFrame.SelectNearest(frames, 32));
    }

    [Fact]
    public void SelectNearest_ReturnsNullWhenNoFrameIsAvailable()
    {
        Assert.Null(PluginIconFrame.SelectNearest([], 32));
        Assert.Null(new DiscoveredPlugin
        {
            Id = "test",
            Name = "test",
            Version = new Version(1, 0),
            MinHostVersion = new Version(1, 0),
            FileName = "test.dll",
            Capabilities = []
        }.GetIconNearest(32));
    }

    private static void AssertPngSize(byte[] png, int expectedSize)
    {
        Assert.Equal(expectedSize, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(expectedSize, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void Read_RejectsOutOfBoundsImageEntries()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PluginIconResolver.EmbeddedIconResourceName)!;
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        var bytes = copy.ToArray();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(6 + (16 * i) + 12), uint.MaxValue);
        }

        using var stream = new MemoryStream(bytes);
        Assert.Null(PluginIconResolver.Read(stream));
    }
}
