using System;
using System.IO;
using System.Reflection;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// Issue #1036: every plugin library ships its own icon as an embedded PNG, so the host never has to guess
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
    public void Read_RejectsPayloadsThatAreNotPng()
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
}
