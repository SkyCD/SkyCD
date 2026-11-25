using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
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
