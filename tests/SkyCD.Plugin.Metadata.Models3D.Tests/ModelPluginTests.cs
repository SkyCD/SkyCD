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

    [Theory]
    [InlineData("external")]
    [InlineData("image")]
    [InlineData("draco")]
    [InlineData("point-cloud")]
    [InlineData("oversize")]
    public async Task MetadataSurvivesWhenPreviewCannotBeProduced(string kind)
    {
        var extension = ".gltf";
        byte[] bytes;
        if (kind == "point-cloud")
        {
            extension = ".ply";
            bytes = ModelFixtures.Ply("ascii", pointCloud: true);
        }
        else if (kind == "oversize")
        {
            extension = ".glb";
            bytes = ModelFixtures.Glb(17 * 1024 * 1024);
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(ModelFixtures.Gltf(kind == "external" ? "absent.bin" : ModelFixtures.DataUri,
                draco: kind == "draco", externalImage: kind == "image"));
        }

        var item = new MediaItem(extension, null, bytes.Length);
        var source = new SequentialSource(bytes);
        var metadata = await new ModelMetadataPlugin().ExtractAsync(item, source, null, default);
        Assert.Equal(3L, metadata.Values.Single(value => value.Key == "VertexCount").Integer);
        Assert.DoesNotContain(metadata.Values, value => value.Key == "Valid");
        Assert.Equal(kind == "oversize", metadata.Values.Any(value => value.Key == "ScanIncomplete"));
        Assert.Equal(kind == "oversize" ? 4 * 1024 * 1024 : bytes.Length, source.BytesRead);
        var preview = await new ModelPreviewPlugin().GeneratePreviewAsync(item, new SequentialSource(bytes), null, default);
        Assert.Empty(preview.Artifacts);
    }

    [Theory]
    [InlineData(".obj", "v 0 1 0\nf 1 1 2")]
    [InlineData(".off", "OFF\n3 1 0\n")]
    [InlineData(".ply", "ply\nformat ascii 1.0\n")]
    [InlineData(".stl", "solid unfinished")]
    [InlineData(".gltf", "{")]
    [InlineData(".glb", "glTF")]
    public async Task InvalidSourcesPublishOnlyFailureFields(string extension, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var item = new MediaItem(extension, null, bytes.Length);
        var metadata = await new ModelMetadataPlugin().ExtractAsync(item, new SequentialSource(bytes), null, default);
        Assert.Equal(3, metadata.Values.Count);
        Assert.False(metadata.Values.Single(value => value.Key == "Valid").Boolean);
        Assert.NotEmpty(metadata.Values.Single(value => value.Key == "FailureCode").Text!);
    }

    [Fact]
    public void PluginProjectContainsNoDiscoveryManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SkyCD.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var plugin = Path.Combine(directory.FullName, "Plugins", "SkyCD.Plugin.Metadata.Models3D");
        Assert.Empty(Directory.EnumerateFiles(plugin, "plugin.json", SearchOption.AllDirectories));
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
