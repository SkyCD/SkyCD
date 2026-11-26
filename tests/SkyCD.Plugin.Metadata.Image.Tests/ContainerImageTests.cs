using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Image.Tests;

public sealed class ContainerImageTests
{
    [Fact]
    public void DescriptorsKeepTheFamiliesAndSchemasStable()
    {
        AssertPair(new RawImagePlugin(), new RawImagePreviewPlugin(), "skycd.image.raw.metadata",
            [".3fr", ".arw", ".cr2", ".crw", ".crx", ".dng", ".gpr", ".kdc", ".nef", ".orf", ".pef", ".raf", ".rw2", ".rwl", ".srw"]);
        AssertPair(new PhotoshopDocumentPlugin(), new PhotoshopDocumentPreviewPlugin(),
            "skycd.image.document.metadata", [".psd", ".psb"]);
        AssertPair(new VectorImagePlugin(), new VectorImagePreviewPlugin(),
            "skycd.image.document.metadata", [".svg", ".svgz", ".ai", ".eps", ".epsf", ".epsi", ".wmf", ".emf"]);
        AssertPair(new PaintDocumentPlugin(), new PaintDocumentPreviewPlugin(),
            "skycd.image.document.metadata", [".xcf", ".kra", ".ora", ".clip", ".afphoto", ".afdesign", ".afpub", ".aftemplate"]);
    }

    [Fact]
    public async Task RawMetadataAndLargestPreviewSurviveTheHostBudget()
    {
        var raw = new byte[20 * 1024 * 1024];
        MetadataFixtures.Tiff(true,
            MetadataFixtures.Short(0x0112, 6, true),
            MetadataFixtures.Ascii(0x010F, "Example camera"),
            MetadataFixtures.Ascii(0x0132, "2024:06:01 12:00:00"),
            MetadataFixtures.Utf16(0x9c9e, "holiday", true)).CopyTo(raw, 0);
        ImageFixtures.Jpeg(12, 10).CopyTo(raw, 2048);
        ImageFixtures.Jpeg(600, 300).CopyTo(raw, 4096);
        var manager = new MediaExtensionManager(
        [
            Provider(new RawImagePlugin()),
            Provider(new RawImagePreviewPlugin()),
            Provider(new ContainerTagsPlugin())
        ]);

        var result = await manager.ProcessAsync(new MediaItem(".dng", "image/tiff", raw.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(raw)));

        Assert.All(result, record => Assert.False(record.Failed));
        var metadata = Assert.Single(result, record => record.Schema!.Id == "skycd.image.raw.metadata").Metadata!;
        Assert.Equal("Example camera", Field(metadata, "CameraMake").Text);
        Assert.NotNull(Field(metadata, "CapturedAt").Timestamp);
        Assert.Equal(90L, Field(metadata, "RotationDegrees").Integer);
        Assert.Single(result.SelectMany(record => record.Metadata?.Values ?? []), value => value.Key == "Tags");
        AssertPreview(Assert.Single(result.Single(record => record.Preview is not null).Preview!.Artifacts),
            "image/jpeg", 256, 512);
    }

    [Fact]
    public async Task PhotoshopReadsTheHeaderAndEmbeddedResource()
    {
        var jpeg = ImageFixtures.Jpeg(50, 20);
        var bytes = PhotoshopHeader(1, 100 + jpeg.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(30), (uint)(40 + jpeg.Length + jpeg.Length % 2));
        "8BIM"u8.CopyTo(bytes.AsSpan(34));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(38), 1036);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(42), (uint)(28 + jpeg.Length));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(46), 1);
        jpeg.CopyTo(bytes, 74);

        var metadata = await Extract(new PhotoshopDocumentPlugin(), ".psd", bytes);
        Assert.Equal(640L, Field(metadata, "Width").Integer);
        Assert.Equal(8L, Field(metadata, "BitDepth").Integer);
        Assert.Equal("RGB", Field(metadata, "ColorMode").Text);
        AssertPreview(Assert.Single((await Preview(new PhotoshopDocumentPreviewPlugin(), ".psd", bytes)).Artifacts),
            "image/jpeg", 50, 20);

        var psb = await Extract(new PhotoshopDocumentPlugin(), ".psb", PhotoshopHeader(2, 26));
        Assert.Equal("psb", Field(psb, "Format").Text);
    }

    [Theory]
    [InlineData(".svg")]
    [InlineData(".svgz")]
    public async Task SvgReadsViewBoxAndRendersTransparency(string extension)
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 400">
              <title>Example</title><desc>Half filled</desc>
              <rect x="400" width="400" height="400" fill="red"/>
            </svg>
            """u8.ToArray();
        if (extension == ".svgz")
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(svg);
            }
            svg = output.ToArray();
        }

        var metadata = await Extract(new VectorImagePlugin(), extension, svg);
        Assert.Equal(800L, Field(metadata, "Width").Integer);
        Assert.Equal("Example", Field(metadata, "Title").Text);
        Assert.Equal("Half filled", Field(metadata, "Comment").Text);
        AssertPreview(Assert.Single((await Preview(new VectorImagePreviewPlugin(), extension, svg)).Artifacts),
            "image/png", 512, 256);
    }

    [Fact]
    public async Task PdfBasedIllustratorUsesPdfPropertiesAndRendersTheFirstPage()
    {
        using var pdf = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        pdf.DocumentInformation.Title = "Vector drawing";
        pdf.DocumentInformation.Author = "Example artist";
        pdf.AddPage(80, 40);
        pdf.AddPage(20, 20);
        var bytes = pdf.Build();

        var metadata = await Extract(new VectorImagePlugin(), ".ai", bytes);
        Assert.Equal("Vector drawing", Field(metadata, "Title").Text);
        Assert.Equal("Example artist", Field(metadata, "Author").Text);
        Assert.Equal(80L, Field(metadata, "Width").Integer);
        var artifact = Assert.Single((await Preview(new VectorImagePreviewPlugin(), ".ai", bytes)).Artifacts);
        Assert.Equal((80, 40), ImageFixtures.Dimensions(artifact.Data));
    }

    [Theory]
    [InlineData(".ai")]
    [InlineData(".eps")]
    [InlineData(".epsi")]
    [InlineData(".epsf")]
    public async Task PostScriptExtractsDscWithoutRendering(string extension)
    {
        var bytes = "%!PS-Adobe-3.0 EPSF-3.0\n%%Title: Sample\n%%Creator: Example\n%%BoundingBox: -10 20 100 60\n"u8.ToArray();
        var metadata = await Extract(new VectorImagePlugin(), extension, bytes);
        Assert.Equal(110L, Field(metadata, "Width").Integer);
        Assert.Equal("Sample", Field(metadata, "Title").Text);
        Assert.Empty((await Preview(new VectorImagePreviewPlugin(), extension, bytes)).Artifacts);
    }

    [Theory]
    [InlineData(".ora", "stack.xml", "Thumbnails/thumbnail.png", "<image w='90' h='70'><stack name='Layers'><layer/></stack></image>")]
    [InlineData(".kra", "maindoc.xml", "preview.png", "<DOC><IMAGE width='90' height='70'/></DOC>")]
    public async Task LayeredArchivePrefersPreviewOverMergedImage(string extension, string xmlName, string preferred, string xml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, xmlName, Encoding.UTF8.GetBytes(xml));
            AddEntry(archive, preferred, ImageFixtures.TransparentPng(48, 24));
            AddEntry(archive, "mergedimage.png", ImageFixtures.OpaquePng(90, 70));
        }

        var bytes = stream.ToArray();
        var metadata = await Extract(new PaintDocumentPlugin(), extension, bytes);
        Assert.Equal(90L, Field(metadata, "Width").Integer);
        if (extension == ".ora")
        {
            Assert.Equal(1L, Field(metadata, "LayerCount").Integer);
        }
        AssertPreview(Assert.Single((await Preview(new PaintDocumentPreviewPlugin(), extension, bytes)).Artifacts),
            "image/png", 48, 24);
    }

    [Fact]
    public async Task ClipOpensTheEmbeddedDatabaseReadOnly()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("clip.sqlite");

        var settings = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        using (var connection = new SqliteConnection(settings.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Canvas (CanvasWidth INTEGER, CanvasHeight INTEGER);
                INSERT INTO Canvas VALUES (640, 480);
                CREATE TABLE CanvasPreview (ImageData BLOB);
                INSERT INTO CanvasPreview VALUES ($preview);
                """;
            command.Parameters.AddWithValue("$preview", ImageFixtures.OpaquePng(60, 40));
            command.ExecuteNonQuery();
        }

        var bytes = new byte[128].Concat(await File.ReadAllBytesAsync(path)).ToArray();
        var metadata = await Extract(new PaintDocumentPlugin(), ".clip", bytes);
        Assert.Equal(640L, Field(metadata, "Width").Integer);
        AssertPreview(Assert.Single((await Preview(new PaintDocumentPreviewPlugin(), ".clip", bytes)).Artifacts),
            "image/jpeg", 60, 40);
    }

    [Fact]
    public async Task AffinitySelectsTheLargestEmbeddedPng()
    {
        var bytes = new byte[128].Concat(ImageFixtures.OpaquePng(16, 12))
            .Concat(ImageFixtures.TransparentPng(96, 48)).ToArray();
        var metadata = await Extract(new PaintDocumentPlugin(), ".afphoto", bytes);
        Assert.Equal(96L, Field(metadata, "Width").Integer);
        AssertPreview(Assert.Single((await Preview(new PaintDocumentPreviewPlugin(), ".afphoto", bytes)).Artifacts),
            "image/png", 96, 48);
    }

    [Fact]
    public async Task XcfReadsThePropertyListAndFlattensTheLayer()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tiny-layer.xcf"));
        var metadata = await Extract(new PaintDocumentPlugin(), ".xcf", bytes);
        Assert.Equal(4L, Field(metadata, "Width").Integer);
        Assert.Equal(1L, Field(metadata, "LayerCount").Integer);
        Assert.Equal("Test layer", Field(metadata, "Comment").Text);
        AssertPreview(Assert.Single((await Preview(new PaintDocumentPreviewPlugin(), ".xcf", bytes)).Artifacts),
            "image/png", 4, 2);
    }

    [Theory]
    [InlineData(".wmf")]
    [InlineData(".emf")]
    public async Task MetafileFrameProducesTwipDimensions(string extension)
    {
        var bytes = extension == ".wmf" ? PlaceableWmf() : EnhancedMetafile();
        var metadata = await Extract(new VectorImagePlugin(), extension, bytes);
        Assert.Equal(1440L, Field(metadata, "Width").Integer);
        Assert.Equal(720L, Field(metadata, "Height").Integer);
        var preview = await Preview(new VectorImagePreviewPlugin(), extension, bytes);
        if (!NativeImageReader.CanRead(extension == ".wmf" ? MagickFormat.Wmf : MagickFormat.Emf))
        {
            Assert.Empty(preview.Artifacts);
        }
        else
        {
            var artifact = Assert.Single(preview.Artifacts);
            Assert.InRange(artifact.Data.Length, 1, 4 * 1024 * 1024);
            var (width, height) = ImageFixtures.Dimensions(artifact.Data);
            Assert.InRange(Math.Max(width, height), 1, 512);
        }
    }

    private static byte[] PlaceableWmf()
    {
        var bytes = new byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x9ac6cdd7);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 1440);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 720);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1440);
        ushort checksum = 0;
        for (var offset = 0; offset < 20; offset += 2)
        {
            checksum ^= BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), checksum);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 0x0300);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 3);
        return bytes;
    }

    private static byte[] EnhancedMetafile()
    {
        var bytes = new byte[108];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 88);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 96);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 48);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 2540);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), 1270);
        " EMF"u8.CopyTo(bytes.AsSpan(40));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(44), 0x10000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(48), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(72), 960);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(76), 480);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), 254);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(84), 127);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88), 14);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92), 20);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(104), 20);
        return bytes;
    }

    [Fact]
    public async Task TruncatedSourcesAndUnavailableArtifactsDoNotFailExtraction()
    {
        Assert.Empty((await Extract(new RawImagePlugin(), ".nef", [1, 2])).Values);
        Assert.Empty((await Preview(new RawImagePreviewPlugin(), ".nef", [1, 2])).Artifacts);
        Assert.Empty((await Extract(new PaintDocumentPlugin(), ".clip", "SQLite format 3\0"u8.ToArray())).Values);
        Assert.Empty((await Preview(new VectorImagePreviewPlugin(), ".svg", "<svg>"u8.ToArray())).Artifacts);
    }

    [Fact]
    public async Task NewProvidersHonorCancellationBeforeReading()
    {
        var source = new ByteArrayMediaSource([1]);
        var token = new CancellationToken(canceled: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RawImagePlugin().ExtractAsync(new MediaItem(".dng", null, 1), source, null, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PaintDocumentPreviewPlugin().GeneratePreviewAsync(new MediaItem(".xcf", null, 1), source, null, token));
    }

    [Theory]
    [InlineData(".dng", false, 4)]
    [InlineData(".dng", true, 32)]
    [InlineData(".psb", false, 4)]
    [InlineData(".psb", true, 32)]
    [InlineData(".xcf", false, 1)]
    [InlineData(".xcf", true, 0)]
    [InlineData(".clip", false, 64)]
    [InlineData(".afphoto", true, 64)]
    public async Task LargeSourcesNeverReadPastTheFormatCap(string extension, bool preview, int mebibytes)
    {
        var source = new SparseMediaSource();
        var item = new MediaItem(extension, null, source.Length);
        var token = CancellationToken.None;
        if (preview)
        {
            IMediaPreviewCapability provider = extension switch
            {
                ".dng" => new RawImagePreviewPlugin(),
                ".psb" => new PhotoshopDocumentPreviewPlugin(),
                _ => new PaintDocumentPreviewPlugin()
            };
            Assert.Empty((await provider.GeneratePreviewAsync(item, source, null, token)).Artifacts);
        }
        else
        {
            IMediaMetadataCapability provider = extension switch
            {
                ".dng" => new RawImagePlugin(),
                ".psb" => new PhotoshopDocumentPlugin(),
                _ => new PaintDocumentPlugin()
            };
            Assert.Empty((await provider.ExtractAsync(item, source, null, token)).Values);
        }
        Assert.Equal(mebibytes * 1024L * 1024, source.BytesRead);
    }

    private sealed class SparseMediaSource : IMediaSource
    {
        private readonly byte[] block = new byte[256 * 1024];

        public long Length => 129 * 1024 * 1024;

        public long BytesRead { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(BytesRead, offset);
            Assert.InRange(count, 1, block.Length);
            BytesRead += count;
            return ValueTask.FromResult((ReadOnlyMemory<byte>)block.AsMemory(0, count));
        }
    }

    [Fact]
    public void MagickLimitsAreAppliedAndMetafileSupportFollowsTheNativeBuild()
    {
        _ = new PaintDocumentPreviewPlugin();
        Assert.Equal(256UL * 1024 * 1024, ResourceLimits.Memory);
        Assert.Equal(0UL, ResourceLimits.Disk);
        Assert.Equal(8192UL, ResourceLimits.Width);
        Assert.Equal(8192UL, ResourceLimits.Height);
        Assert.Equal(30UL, ResourceLimits.Time);
        foreach (var coder in new[] { MagickFormat.Wmf, MagickFormat.Emf })
        {
            Assert.Equal(MagickNET.SupportedFormats.Any(format => format.Format == coder && format.SupportsReading),
                NativeImageReader.CanRead(coder));
        }
    }

    private static byte[] PhotoshopHeader(ushort version, int length)
    {
        var bytes = new byte[length];
        "8BPS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), version);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(14), 480);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(18), 640);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), 8);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24), 3);
        return bytes;
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] bytes)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(bytes);
    }

    private static MediaProvider Provider(SkyCD.SDK.Capabilities.IPluginCapability capability) =>
        new("SkyCD.Plugin.Image", "1.0.0", capability);

    private static Task<MediaExtractionResult> Extract(IMediaMetadataCapability capability, string extension, byte[] bytes) =>
        capability.ExtractAsync(new MediaItem(extension, null, bytes.Length), new ByteArrayMediaSource(bytes), null, CancellationToken.None);

    private static Task<MediaPreviewResult> Preview(IMediaPreviewCapability capability, string extension, byte[] bytes) =>
        capability.GeneratePreviewAsync(new MediaItem(extension, null, bytes.Length), new ByteArrayMediaSource(bytes), null, CancellationToken.None);

    private static MediaMetadataValue Field(MediaExtractionResult result, string name) =>
        Assert.Single(result.Values, value => value.Key == name);

    private static void AssertPreview(MediaPreviewArtifact artifact, string mimeType, int width, int height)
    {
        Assert.Equal(mimeType, artifact.ContentType);
        Assert.InRange(artifact.Data.Length, 1, 4 * 1024 * 1024);
        Assert.Equal((width, height), ImageFixtures.Dimensions(artifact.Data.ToArray()));
    }

    private static void AssertPair(IMediaMetadataCapability metadata, IMediaPreviewCapability preview,
        string schema, string[] extensions)
    {
        Assert.Equal(extensions, metadata.Descriptor.Extensions);
        Assert.Equal(extensions, preview.Descriptor.Extensions);
        Assert.Equal(schema, metadata.Descriptor.Schema.Id);
        Assert.Equal(1, metadata.Descriptor.Schema.Version);
        Assert.Equal("skycd.image.container.preview", preview.Descriptor.Schema.Id);
        Assert.Equal(1, preview.Descriptor.Schema.Version);
        Assert.True(preview.Descriptor.IndexTimePreview);
    }
}
