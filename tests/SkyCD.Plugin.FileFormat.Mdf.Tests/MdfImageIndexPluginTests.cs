using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.Mdf.Models;

namespace SkyCD.Plugin.FileFormat.Mdf.Tests;

public sealed class MdfImageIndexPluginTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(MdfImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-mdf", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public void OpenFormats_IncludeMds_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new MdfImageIndexPlugin()]);

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "skycd-mdf" && format.Extensions.Contains(".mds"));
        Assert.DoesNotContain(service.GetSaveFormats(), format => format.FormatId == "skycd-mdf");
        Assert.Equal("skycd-mdf", service.ResolveFormatId(null, "catalog.mds", forWrite: false));
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyMdfFormat()
    {
        var service = new FileFormatManager([new MdfImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-mdf",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2048, 0)]
    [InlineData(2352, 16)]
    [InlineData(2352, 24)]
    [InlineData(2336, 8)]
    public async Task ReadAsync_SectorLayout_ExposesIsoFilesystem(int sectorSize, int dataOffset)
    {
        using var temp = new TempDirectory();
        var image = MdfTestImage.Repack(MdfTestImage.BuildIso(), sectorSize, dataOffset);
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", image, MdfTestImage.BuildMds());

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP") && Equals(row["kind"], "folder"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("FIXTURE.TXT", file["name"]);
        Assert.Equal(MdfTestImage.FixtureContent.Length.ToString(), file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_OpenedFromMdf_ResolvesDescriptorAndData()
    {
        using var temp = new TempDirectory();
        var image = MdfTestImage.Repack(MdfTestImage.BuildIso(), 2352, 16);
        MdfTestImage.WriteSet(temp.Path, "catalog", image, MdfTestImage.BuildMds());
        var mdfPath = Path.Combine(temp.Path, "catalog.mdf");

        var result = await ReadAsync(mdfPath);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/README.TXT"));
    }

    [Fact]
    public async Task ReadAsync_MdfWithoutDescriptor_StillReadsData()
    {
        using var temp = new TempDirectory();
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", MdfTestImage.BuildIso(), MdfTestImage.BuildMds());
        File.Delete(path);
        var mdfPath = Path.Combine(temp.Path, "catalog.mdf");

        var result = await ReadAsync(mdfPath);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/README.TXT"));
    }

    [Fact]
    public async Task ReadAsync_MissingCompanion_FailsWithCompanionName()
    {
        using var temp = new TempDirectory();
        var descriptorPath = Path.Combine(temp.Path, "catalog.mds");
        await File.WriteAllBytesAsync(descriptorPath, MdfTestImage.BuildMds());

        var result = await ReadAsync(descriptorPath);

        Assert.False(result.Success);
        Assert.Contains("catalog.mdf", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DamagedDescriptor_FailsGracefully()
    {
        using var temp = new TempDirectory();
        var descriptor = new string('x', 88);
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", MdfTestImage.BuildIso(),
            Encoding.ASCII.GetBytes(descriptor));

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("descriptor", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedDescriptorVersion_Fails()
    {
        using var temp = new TempDirectory();
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", MdfTestImage.BuildIso(),
            MdfTestImage.BuildMds(version: "9.99"));

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("version", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_UnknownMediumType_Fails()
    {
        using var temp = new TempDirectory();
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", MdfTestImage.BuildIso(),
            MdfTestImage.BuildMds(mediumType: 0x77));

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("medium", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_NonIsoPayload_FailsWithIsoMessage()
    {
        using var temp = new TempDirectory();
        var payload = new byte[100_000];
        Random.Shared.NextBytes(payload);
        var path = MdfTestImage.WriteSet(temp.Path, "catalog", payload, MdfTestImage.BuildMds());

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("ISO 9660", result.Error);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_AndOpensTheirContent()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var before = TemporaryEntryFiles();

        await using var set = await new MdfImageIndexPlugin().GetWorkItemsAsync(RequestFor(path));

        Assert.Equal(2, set.Items.Count(item => item.Name.EndsWith(".TXT", StringComparison.Ordinal)));
        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        Assert.Equal(MdfTestImage.FixtureContent.Length, item.SizeBytes);
        await using (var content = await item.OpenSource(CancellationToken.None))
        using (var reader = new StreamReader(content, Encoding.UTF8))
        {
            Assert.Equal(MdfTestImage.FixtureContent, await reader.ReadToEndAsync());
        }

        Assert.Equal(before, TemporaryEntryFiles());
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonMdfSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        await using var set = await new MdfImageIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
        {
            FileName = "notes.txt",
            SizeBytes = stream.Length,
            OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
        });

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MissingCompanion_ReturnsEmptySet()
    {
        using var temp = new TempDirectory();
        var descriptorPath = Path.Combine(temp.Path, "catalog.mds");
        await File.WriteAllBytesAsync(descriptorPath, MdfTestImage.BuildMds());

        await using var set = await new MdfImageIndexPlugin().GetWorkItemsAsync(RequestFor(descriptorPath));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new MdfImageIndexPlugin(new MdfIndexLimits(MaxEntries: 100, MaxEntryBytes: 8));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.Equal("ROOT/README.TXT", item.Name);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_LimitsContributedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new MdfImageIndexPlugin(new MdfIndexLimits(MaxEntries: 1));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        Assert.Single(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_TotalBudget_BoundsMaterializedBytes()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new MdfImageIndexPlugin(new MdfIndexLimits(MaxTotalBytes: 20));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.True(item.SizeBytes <= 20);
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MdfImageIndexPlugin().GetWorkItemsAsync(RequestFor(path), cancellation.Token));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_RejectsNonPositiveLimits(int maxEntries, long maxEntryBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MdfImageIndexPlugin(new MdfIndexLimits(maxEntries, maxEntryBytes)));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new MdfImageIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(path));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Fixture_ReadsThroughCommittedFiles()
    {
        var descriptorPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mdf", "catalog-sample.mds");
        Assert.True(File.Exists(descriptorPath));
        Assert.True(File.Exists(Path.ChangeExtension(descriptorPath, ".mdf")));

        var result = await ReadAsync(descriptorPath);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    private static async Task<FileFormatReadResult> ReadAsync(string path)
    {
        await using var source = File.OpenRead(path);
        return await new MdfImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-mdf",
            Source = source,
            FileName = Path.GetFileName(path),
            SourcePath = path
        });
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static string WriteCatalog(string directory) =>
        MdfTestImage.WriteSet(directory, "catalog",
            MdfTestImage.Repack(MdfTestImage.BuildIso(), 2352, 16), MdfTestImage.BuildMds());

    private static IndexWorkItemRequest RequestFor(string descriptorPath) => new()
    {
        FileName = Path.GetFileName(descriptorPath),
        SizeBytes = new FileInfo(descriptorPath).Length,
        SourcePath = descriptorPath,
        OpenSource = _ => Task.FromResult<Stream>(File.OpenRead(descriptorPath))
    };

    private static List<string> TemporaryEntryFiles() =>
        Directory.Exists(Path.GetTempPath())
            ? Directory.GetFiles(Path.GetTempPath(), "skycd-mdf-entry-*.tmp").ToList()
            : [];

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"skycd-mdf-tests-{Guid.NewGuid():N}");
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

    /// <summary>Lets the plugin dispose its stream while the test keeps ownership of the memory stream.</summary>
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
