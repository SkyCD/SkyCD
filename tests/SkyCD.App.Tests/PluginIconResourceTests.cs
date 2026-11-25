using System.IO;
using SkyCD.Plugin.FileFormat.Json;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginIconResourceTests
{
    [Fact]
    public void PluginAssembly_ContainsTheMultiresolutionIcon()
    {
        Assert.Null(typeof(JsonCatalogPlugin).Assembly.GetManifestResourceStream("SkyCD.Plugin.Icon.png"));
        using var stream = typeof(JsonCatalogPlugin).Assembly.GetManifestResourceStream("SkyCD.Plugin.Icon.ico");
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(14, reader.ReadUInt16());

        int[] expected = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];
        foreach (var size in expected)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            Assert.Equal(size, width == 0 ? 256 : width);
            Assert.Equal(size, height == 0 ? 256 : height);
            reader.ReadBytes(14);
        }
    }
}
