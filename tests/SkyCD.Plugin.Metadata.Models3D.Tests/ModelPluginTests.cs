using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.Models3D.Tests;

public sealed class ModelPluginTests
{
    public static TheoryData<string, byte[], long> Renderable => new()
    {
        { ".obj", Encoding.UTF8.GetBytes(ModelFixtures.Obj), 1 },
        { ".off", Encoding.UTF8.GetBytes(ModelFixtures.Off), 2 },
        { ".stl", Encoding.UTF8.GetBytes(ModelFixtures.Stl), 1 },
        { ".stl", ModelFixtures.BinaryStl(), 1 },
        { ".ply", ModelFixtures.Ply("ascii"), 1 },
        { ".ply", ModelFixtures.Ply("binary_little_endian"), 1 },
        { ".ply", ModelFixtures.Ply("binary_big_endian"), 1 },
        { ".gltf", Encoding.UTF8.GetBytes(ModelFixtures.Gltf(ModelFixtures.DataUri)), 1 },
        { ".glb", ModelFixtures.Glb(), 1 }
    };

    [Theory]
    [MemberData(nameof(Renderable))]
    public async Task HostDiscoversReadersAndProducesDeterministicPreviews(string extension, byte[] bytes, long triangles)
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ModelMetadataPlugin).Assembly);
        Assert.Equal("SkyCD.Plugin.Models", plugin.Id);
        Assert.Equal(2, plugin.Capabilities.Count);
        var manager = new MediaExtensionManager(plugin.Capabilities.Select(capability =>
            new MediaProvider(plugin.Id, "1.0.0", capability)).ToArray());
        var item = new MediaItem(extension, null, bytes.Length);
        var results = await manager.ProcessAsync(item, _ => Task.FromResult<Stream>(new MemoryStream(bytes)));
        Assert.All(results, result => Assert.False(result.Failed));
        var fields = results.Single(result => result.Metadata is not null).Metadata!.Values;
        Assert.Equal(triangles, fields.Single(value => value.Key == "TriangleCount").Integer);
        Assert.DoesNotContain(fields, value => value.Key is "Valid" or "ScanIncomplete");
        Assert.Equal(extension is ".gltf" or ".glb" ? "2 0 0" : "0 0 0",
            fields.Single(value => value.Key == "BoundingBoxMin").Text);
        if (extension == ".glb")
            Assert.Equal(1L, fields.Single(value => value.Key == "TextureCount").Integer);
        if (extension == ".obj")
            Assert.Equal("absent.mtl", fields.Single(value => value.Key == "MtlLibraries").Text);

        var artifact = Assert.Single(results.Single(result => result.Preview is not null).Preview!.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.InRange(artifact.Data.Length, 24, 4 * 1024 * 1024);
        Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(artifact.Data.Span[16..]), 1, 512);
        Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(artifact.Data.Span[20..]), 1, 512);
        var replay = await new ModelPreviewPlugin().GeneratePreviewAsync(item, new SequentialSource(bytes), null, default);
        Assert.Equal(artifact.Data.ToArray(), Assert.Single(replay.Artifacts).Data.ToArray());
    }

    private sealed class SequentialSource(byte[] data) : IMediaSource
    {
        public int BytesRead { get; private set; }
        public long Length => data.Length;

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(BytesRead, offset);
            Assert.InRange(count, 1, data.Length - BytesRead);
            BytesRead += count;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory((int)offset, count));
        }
    }
}
