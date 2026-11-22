using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.Metadata.DiskImages;
using SkyCD.Plugin.Metadata.DiskImages.Daa.Import;
using SkyCD.Plugin.Metadata.DiskImages.Daa.Parsing;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class DaaImageIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_NonDaaSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_SkipsUnsafeEntryPaths()
    {
        var entries = new[]
        {
            new DaaEntryInfo("../escape.txt", false, 4, null),
            new DaaEntryInfo("/absolute.txt", false, 4, null),
            new DaaEntryInfo("folder/../../escape2.txt", false, 4, null),
            new DaaEntryInfo("SAFE.TXT", false, 4, null)
        };
        using var source = SingleVolumeSource(("ROOT/FIXTURE.TXT", "fixture!"));

        await using var set = await DaaIndexWorkItems.CreateAsync(RequestFor(source, "image.daa"),
            new FakeEntryReader(entries), CancellationToken.None);

        var item = Assert.Single(set.Items);
        Assert.Equal("SAFE.TXT", item.Name);
    }

    [Fact]
    public async Task GetWorkItemsAsync_OpensContainedFileContent()
    {
        using var source = SingleVolumeSource(("ROOT/DEEP/FIXTURE.TXT", "hello daa"));

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(source, "image.daa"));

        var item = Assert.Single(set.Items);
        Assert.Equal("ROOT/DEEP/FIXTURE.TXT", item.Name);
        Assert.Equal(9, item.SizeBytes);
        await using var content = await item.OpenSource(CancellationToken.None);
        using var reader = new StreamReader(content, Encoding.UTF8);
        Assert.Equal("hello daa", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task GetWorkItemsAsync_MultipartVolumes_OpenContentAcrossVolumes()
    {
        using var temp = new TempDirectory();
        var text = new string('x', 200_000);
        var iso = DaaTestImage.CreateIso(("ROOT/DEEP/LARGE.TXT", text));
        var firstPath = DaaTestImage.WriteMultipart(temp.Path, "image", iso);
        var request = new IndexWorkItemRequest
        {
            FileName = Path.GetFileName(firstPath),
            SourcePath = firstPath,
            SizeBytes = new FileInfo(firstPath).Length,
            OpenSource = _ => Task.FromResult<Stream>(File.OpenRead(firstPath))
        };

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(request);

        var item = Assert.Single(set.Items);
        Assert.Equal(200_000, item.SizeBytes);
        await using var content = await item.OpenSource(CancellationToken.None);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        Assert.Equal(Encoding.UTF8.GetBytes(text), buffer.ToArray());
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var source = SingleVolumeSource(("ROOT/FIXTURE.TXT", "fixture!"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(source, "image.daa"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedItems()
    {
        using var source = SingleVolumeSource(("ROOT/DEEP/FIXTURE.TXT", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new DiskImagesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(source, "image.daa"));

        var entry = Assert.Single(indexed);
        Assert.Equal("ROOT/DEEP/FIXTURE.TXT", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value => value.Key == "indexed" && value.Text == "yes");
    }

    private static MemoryStream SingleVolumeSource(params (string Path, string Content)[] files) =>
        new(DaaTestImage.CreateSingleVolume(DaaTestImage.CreateIso(files)));

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    private static List<string> TemporaryImageDirectories() =>
        Directory.Exists(Path.GetTempPath())
            ? Directory.GetDirectories(Path.GetTempPath(), "skycd-daa-*").ToList()
            : [];

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"skycd-daa-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class FakeEntryReader(IReadOnlyCollection<DaaEntryInfo> entries) : IDaaEntryReader
    {
        public IReadOnlyCollection<DaaEntryInfo> ReadEntries(Stream image) => entries;
    }

    /// <summary>Lets the plugin dispose its source stream while the test keeps ownership of the memory stream.</summary>
    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) => inner.Dispose();
    }

    private sealed class TestMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor { get; } = new(
            Extensions: ["*"],
            ContentTypes: ["*"],
            Schema: new MediaMetadataSchema("test.schema", 1,
                [new MediaMetadataField("indexed", MediaMetadataType.Text)]),
            Priority: 0,
            IsOptIn: false);

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new MediaExtractionResult([
                new MediaMetadataValue("indexed", MediaMetadataType.Text, Text: "yes")
            ]));
        }
    }
}
